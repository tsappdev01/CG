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
    [switch] $OverwriteAppSettings
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

if (-not (Test-Path $PublishPath)) { throw "Nothing to deploy: $PublishPath does not exist." }
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    throw "App pool '$AppPoolName' does not exist. Run scripts/deploy/New-CgDiparkSite.ps1 on this server first."
}
if (-not (Test-Path $SitePath)) { New-Item -ItemType Directory -Path $SitePath -Force | Out-Null }

$excludeDirs  = @((Join-Path $SitePath 'wwwroot\uploads'))
$excludeFiles = @()
if (-not $OverwriteAppSettings) { $excludeFiles += (Join-Path $SitePath 'appsettings.json') }

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
}
finally {
    # finally, not after the copy: a failed copy that left the pool stopped would take the site
    # down until someone noticed.
    Write-Host "Starting app pool '$AppPoolName'..."
    Start-WebAppPool -Name $AppPoolName
}

Write-Host "Deployed."
