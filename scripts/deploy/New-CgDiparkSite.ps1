<#
.SYNOPSIS
    Creates the cg.dipark.com site and app pool on UATWEB01. Run once, elevated. Re-runnable.

.DESCRIPTION
    Everything here is guarded, so running it twice changes nothing.

    The app pool is "No Managed Code" because this is ASP.NET Core: the runtime is in the app, not
    in IIS, and leaving it on v4.0 loads the .NET Framework CLR into the worker for nothing.

    What this does NOT do, and you have to:

      1. .NET 10 Hosting Bundle. Not the SDK, not the runtime -- the Hosting Bundle, which carries
         the ASP.NET Core Module that lets IIS hand requests to the app. Without it the site
         answers 500.19 or 500.31. Install it, then `iisreset`.
      2. DNS. cg.dipark.com has to resolve to this server.
      3. The HTTPS certificate. The binding below is created only if a certificate matching the
         host name is already in LocalMachine\My -- otherwise only the HTTP binding is made and
         this says so.
      4. The app pool's secrets. See README "On the IIS server, set them as environment variables
         on the app pool" -- they override appsettings.json and survive a deploy, which is why
         that is the better place for them than the file.
      5. Write access for the app pool identity to wwwroot\uploads and to
         C:\ProgramData\CGTOOL\DataProtectionKeys. Without the second, every restart logs everyone
         out; without the first, uploads fail.
#>
[CmdletBinding()]
param(
    [string] $SiteName    = 'DIP CGS',
    [string] $AppPoolName = 'DIP CGS',
    [string] $SitePath    = 'C:\Websites\CGDIP',
    [string] $HostName    = 'cg.dipark.com',
    [string] $AspNetCoreEnvironment = 'Production',
    # Off by default: this deployment keeps its configuration in the server's appsettings.json,
    # and an environment variable of the same name would silently win over it. Pass the switch
    # only where the variable is wanted instead of the file.
    [string] $PublicBaseUrl = '',
    [switch] $SetPublicBaseUrlVariable
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

if (-not (Test-Path $SitePath)) {
    New-Item -ItemType Directory -Path $SitePath -Force | Out-Null
    Write-Host "Created $SitePath"
}
New-Item -ItemType Directory -Path (Join-Path $SitePath 'wwwroot\uploads') -Force | Out-Null

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Write-Host "Created app pool $AppPoolName"
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'
# The app holds a SignalR circuit per open tab; recycling on a timer drops them mid-session.
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name recycling.periodicRestart.time -Value '00:00:00'

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $AppPoolName `
                -HostHeader $HostName -Port 80 | Out-Null
    Write-Host "Created site $SiteName on http://$HostName"
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath

# A wildcard certificate's DnsNameList holds *.dipark.com, not cg.dipark.com, so an exact match
# alone would report "no certificate" for the very certificate the site is already using.
$wildcard = '*.' + ($HostName -replace '^[^.]+\.', '')
$cert = Get-ChildItem Cert:\LocalMachine\My |
        Where-Object { ($_.DnsNameList -contains $HostName -or $_.DnsNameList -contains $wildcard) `
                       -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1

if ($cert) {
    if (-not (Get-WebBinding -Name $SiteName -Protocol https -HostHeader $HostName -ErrorAction SilentlyContinue)) {
        # sslFlags 1 = SNI, so this certificate does not have to be the only one on port 443.
        New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1
        # Through the binding itself rather than the IIS:\SslBindings provider: the provider path
        # for an SNI binding is easy to get subtly wrong and fails in ways that look like a bad
        # certificate.
        (Get-WebBinding -Name $SiteName -Protocol https -HostHeader $HostName).AddSslCertificate($cert.Thumbprint, 'My')
        Write-Host "Bound https://$HostName to certificate $($cert.Thumbprint) (expires $($cert.NotAfter))"
    }
} else {
    Write-Warning "No valid certificate for $HostName in LocalMachine\My -- only the HTTP binding was created. Install the certificate, then re-run this script to add HTTPS."
}

$identity = "IIS AppPool\$AppPoolName"
foreach ($path in @((Join-Path $SitePath 'wwwroot\uploads'), 'C:\ProgramData\CGTOOL\DataProtectionKeys')) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $acl = Get-Acl $path
    $acl.SetAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
        $identity, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    Set-Acl $path $acl
    Write-Host "Granted Modify on $path to $identity"
}

# ---- app pool environment variables --------------------------------------------------------
# ASP.NET Core defaults to Production only when nothing says otherwise, and plenty of things do:
# a web.config carried over from a Debug publish, a machine-wide variable, a value someone set
# while testing. Left on Development the site serves the developer exception page to the world
# and reads appsettings.Development.json -- where Branding:PublicBaseUrl is localhost, so every
# reminder e-mail would link people to their own machine. Set it explicitly rather than hoping.
#
# On the app pool, not in web.config: publish regenerates web.config on every deploy, so a value
# put there lasts until the next one. These survive.
function Set-AppPoolEnvironmentVariable {
    param([string] $Pool, [string] $Name, [string] $Value)

    $filter = "system.applicationHost/applicationPools/add[@name='$Pool']/environmentVariables"
    # Remove-then-add rather than a get/compare: the collection is awkward to read back, and
    # removing something that is not there is harmless with -ErrorAction SilentlyContinue.
    Remove-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $filter `
        -name '.' -AtElement @{name=$Name} -ErrorAction SilentlyContinue
    Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter $filter `
        -name '.' -value @{name=$Name; value=$Value}
    Write-Host "  $Name = $Value"
}

Write-Host "Setting app pool environment variables..."
# ASPNETCORE_ENVIRONMENT is the one that has to be here. It is a host setting rather than app
# configuration -- appsettings.json cannot name the environment that decides which appsettings
# file is read -- and left unset the app can end up on Development, serving the developer
# exception page and its stack traces to anyone who can provoke an error.
Set-AppPoolEnvironmentVariable -Pool $AppPoolName -Name 'ASPNETCORE_ENVIRONMENT' -Value $AspNetCoreEnvironment

if ($SetPublicBaseUrlVariable -and $PublicBaseUrl) {
    Set-AppPoolEnvironmentVariable -Pool $AppPoolName -Name 'Branding__PublicBaseUrl' -Value $PublicBaseUrl
}
else {
    Write-Host "  Branding:PublicBaseUrl left to appsettings.json -- set it there to the site's own address."
}

Write-Host ""
Write-Host "Site ready. Still to do by hand: the .NET 10 Hosting Bundle, DNS, and the values in"
Write-Host "$SitePath\appsettings.json -- the connection string, the Entra secret, and"
Write-Host "Branding:PublicBaseUrl. Deploys leave that file alone once it exists."
