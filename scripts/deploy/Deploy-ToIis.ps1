<#
.SYNOPSIS
    Copies a published build into an IIS site, stopping the app pool for the copy.

.DESCRIPTION
    Two things in the site folder are NOT the build's to manage, and both are excluded so the
    mirror does not delete them:

      wwwroot\uploads   Profile pictures, trade licences, signatures and declaration attachments.
                        They are uploaded by users, exist only on this server, and are gitignored,
                        so a plain /MIR would delete every one of them on the first deploy.

      appsettings.json  The server's copy holds the real connection string and the Entra client
                        secret. The repository's copy has empty placeholders, so copying it over
                        would take the site down until someone retyped them. Pass
                        -OverwriteAppSettings to deploy it anyway, for a change to a non-secret
                        setting -- and expect to put the secrets back.

    The data protection key ring is not a concern: it defaults to
    C:\ProgramData\CGTOOL\DataProtectionKeys, outside the site folder, so it survives regardless.
    If DataProtection:KeyRingPath has been pointed somewhere inside the site, exclude it too or
    every signed-in user is logged out by each deploy.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishPath,
    [Parameter(Mandatory)] [string] $SitePath,
    [Parameter(Mandatory)] [string] $AppPoolName,
    [int] $RequiredRuntimeMajor = 10,
    [switch] $OverwriteAppSettings
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

if (-not (Test-Path $PublishPath)) { throw "Nothing to deploy: $PublishPath does not exist." }
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    throw "App pool '$AppPoolName' does not exist. Run scripts/deploy/New-CgDiparkSite.ps1 on this server first."
}
if (-not (Test-Path $SitePath)) { New-Item -ItemType Directory -Path $SitePath -Force | Out-Null }

# Before anything is stopped or overwritten. dotnet publish here is framework-dependent, so the
# matching ASP.NET Core runtime has to be on the server -- and if it is not, this mirror replaces
# a working site with one that cannot start. That is exactly what happened the first time: the
# site had been published self-contained by hand, carrying its own runtime, and /MIR removed it.
# Failing here leaves the running site untouched.
# A self-contained publish brings its own runtime, so nothing needs to be installed on the
# server and this check does not apply. Detected from the output rather than from a flag: the
# two cannot then disagree. System.Private.CoreLib.dll is only ever in a self-contained build.
$isSelfContained = Test-Path (Join-Path $PublishPath 'System.Private.CoreLib.dll')

$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if ($isSelfContained) {
    Write-Host "Self-contained publish -- the app carries its own runtime, nothing required on the server."
}
elseif (Test-Path $dotnet) {
    $runtimes = & $dotnet --list-runtimes 2>$null
    $hasRuntime = $runtimes | Where-Object { $_ -match "^Microsoft\.AspNetCore\.App $RequiredRuntimeMajor\." }
    if (-not $hasRuntime) {
        $found = ($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App' } |
                  ForEach-Object { ($_ -split ' ')[1] }) -join ', '
        throw ("This build needs the ASP.NET Core $RequiredRuntimeMajor runtime and the server does not have it. " +
               "Found: $found. Install the .NET $RequiredRuntimeMajor.0 HOSTING BUNDLE (not the SDK, not the " +
               "standalone runtime -- the bundle also carries the ASP.NET Core Module IIS needs) from " +
               "https://dotnet.microsoft.com/download/dotnet/$RequiredRuntimeMajor.0, then run iisreset. " +
               "Nothing has been changed; the site is still running whatever it was running.")
    }
    Write-Host "ASP.NET Core $RequiredRuntimeMajor runtime present."
}
else {
    Write-Warning "No dotnet host at $dotnet, so the runtime could not be checked before deploying."
}

$excludeDirs  = @((Join-Path $SitePath 'wwwroot\uploads'))
$excludeFiles = @()
# Only exclude it when the server already HAS one. On a brand-new site folder there is nothing
# to protect, and excluding it unconditionally means the very first deploy to a new site copies
# no appsettings.json at all -- the app then starts with no connection string and no Entra
# settings, which looks like a broken build rather than a missing file.
$destinationAppSettings = Join-Path $SitePath 'appsettings.json'
if (-not $OverwriteAppSettings -and (Test-Path $destinationAppSettings)) {
    $excludeFiles += $destinationAppSettings
}
elseif (-not $OverwriteAppSettings) {
    Write-Host "No appsettings.json on the server yet, so the build's copy is being deployed." `
               "Put the real connection string and secrets in it (or better, in the app pool's" `
               "environment variables) -- later deploys will leave it alone."
}

Write-Host "Stopping app pool '$AppPoolName'..."
if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Stopped') {
    Stop-WebAppPool -Name $AppPoolName
    # Stop-WebAppPool returns before the worker process has exited, and a w3wp still holding
    # CGTOOL.Web.dll makes the copy fail with "being used by another process".
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Stopped' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -ne 'Stopped') {
        throw "App pool '$AppPoolName' did not stop within 30 seconds."
    }
}

try {
    # dotnet publish copies every appsettings.*.json, so the Development overrides ride along to
    # a server that has no business reading them. Excluded from the copy, and removed from the
    # destination if an earlier deploy already put one there: if anything ever sets this site to
    # Development, that file would win over appsettings.json, and it holds a localhost address.
    $strayDevSettings = Join-Path $SitePath 'appsettings.Development.json'
    if (Test-Path $strayDevSettings) {
        Remove-Item $strayDevSettings -Force
        Write-Host "Removed $strayDevSettings -- development overrides do not belong on a server."
    }
    $excludeFiles += (Join-Path $SitePath 'appsettings.Development.json')

    $robocopyArgs = @($PublishPath, $SitePath, '/MIR', '/NFL', '/NDL', '/NJH', '/NP', '/R:2', '/W:2')
    foreach ($d in $excludeDirs)  { $robocopyArgs += @('/XD', $d) }
    foreach ($f in $excludeFiles) { $robocopyArgs += @('/XF', $f) }

    Write-Host "Copying $PublishPath -> $SitePath"
    if ($excludeFiles) { Write-Host "  keeping the server's own: $($excludeFiles -join ', ')" }
    Write-Host "  keeping uploads in: $($excludeDirs -join ', ')"

    & robocopy @robocopyArgs
    # robocopy does not use 0 for success: 0-7 are all fine (1 = files copied, 2 = extras found,
    # 3 = both, and so on). Only 8 and above are failures, so the exit code has to be read rather
    # than trusted, or every successful deploy fails the task.
    $code = $LASTEXITCODE
    if ($code -ge 8) { throw "robocopy failed with exit code $code." }
    Write-Host "robocopy exit code $code (under 8 = success)."

    # Clear it. The PowerShell task reads $LASTEXITCODE when the script ends, and robocopy leaves
    # a non-zero code there on SUCCESS -- 1 for "files were copied", 2 for "extras found", 3 for
    # both. Left as it is, a deploy that worked is reported as "PowerShell exited with code 1".
    $global:LASTEXITCODE = 0
}
finally {
    # finally, not after the copy: a failed copy that left the pool stopped would take the site
    # down until someone noticed.
    Write-Host "Starting app pool '$AppPoolName'..."
    Start-WebAppPool -Name $AppPoolName
}

Write-Host "Deployed."

# Explicit, so the task cannot inherit an exit code from anything above.
exit 0
