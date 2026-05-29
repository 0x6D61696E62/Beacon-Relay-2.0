[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServiceName = 'BeaconRelay',
    [string]$SiteName = 'BeaconRelay Admin',
    [string]$HostName = 'beaconrelay.local',
    [int]$KestrelPort = 8080,
    [int]$LpdPort = 515,
    [string]$AppRoot = 'C:\Apps\BeaconRelay',
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$AdminUsername = 'admin',
    [string]$AdminPassword = 'change-me-now',
    [string]$DatabasePath = 'C:\Apps\BeaconRelay\db\beacon-relay.db',
    [string]$InboxPath = 'C:\Apps\BeaconRelay\data\inbox',
    [string]$RoutedPath = 'C:\Apps\BeaconRelay\data\routed',
    [string]$ServiceUser = '',
    [string]$ServicePassword = '',
    [string]$CertificateThumbprint = '',
    [string]$AllowedLpdRemoteAddresses = 'LocalSubnet',
    [switch]$AutoInstallIisProxyModules,
    [switch]$CreateSelfSignedCert,
    [switch]$SkipIis,
    [switch]$SkipFirewall,
    [switch]$NoPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Ensure-Admin {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script in an elevated PowerShell session (Run as Administrator).'
    }
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, 'Create directory')) {
            New-Item -Path $Path -ItemType Directory -Force | Out-Null
        }
    }
}

function Test-IisModuleAvailable {
    return [bool](Get-Module -ListAvailable -Name WebAdministration)
}

function Test-ArrInstalled {
    # ARR installs its schema file here; if absent the proxy config section doesn't exist.
    $schemaPath = Join-Path $env:SystemRoot 'System32\inetsrv\config\schema\arr_schema.xml'
    return (Test-Path $schemaPath)
}

function Ensure-IisProxyModules {
    if (Test-ArrInstalled) {
        return $true
    }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Warning 'winget is not available. Install IIS URL Rewrite + ARR manually.'
        return $false
    }

    if ($PSCmdlet.ShouldProcess('IIS URL Rewrite 2.1', 'Install module via winget')) {
        & winget install --id Microsoft.IIS.URLRewrite --exact --silent --accept-package-agreements --accept-source-agreements --scope machine
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'Failed to install IIS URL Rewrite via winget.'
            return $false
        }
    }

    if ($PSCmdlet.ShouldProcess('IIS ARR 3.0', 'Install module via winget')) {
        & winget install --id Microsoft.IIS.ApplicationRequestRouting --exact --silent --accept-package-agreements --accept-source-agreements --scope machine
        if ($LASTEXITCODE -ne 0) {
            Write-Warning 'Failed to install IIS ARR via winget.'
            return $false
        }
    }

    return (Test-ArrInstalled)
}

function New-DeploymentCertificate {
    param([string]$DnsName)
    $existing = Get-ChildItem 'Cert:\LocalMachine\My' |
        Where-Object { $_.DnsNameList.Unicode -contains $DnsName } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
    if ($existing) {
        Write-Host "  Re-using existing certificate for $DnsName (thumbprint: $($existing.Thumbprint))" -ForegroundColor DarkGray
        return $existing.Thumbprint
    }
    $cert = New-SelfSignedCertificate `
        -DnsName $DnsName `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -FriendlyName "BeaconRelay $DnsName" `
        -NotAfter (Get-Date).AddYears(3)
    Write-Host "  Created self-signed certificate: $($cert.Thumbprint)" -ForegroundColor DarkGray
    return $cert.Thumbprint
}

function Ensure-IisFeatures {
    if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
        $features = @('Web-Server', 'Web-WebServer', 'Web-Common-Http', 'Web-Static-Content', 'Web-Default-Doc', 'Web-Http-Errors', 'Web-Http-Redirect', 'Web-Health', 'Web-Http-Logging', 'Web-Performance', 'Web-Stat-Compression', 'Web-Security', 'Web-Filtering', 'Web-App-Dev', 'Web-Net-Ext45', 'Web-Asp-Net45', 'Web-Mgmt-Tools')
        if ($PSCmdlet.ShouldProcess('Windows Features', 'Install IIS role features')) {
            Install-WindowsFeature -Name $features | Out-Null
        }
    }
    else {
        Write-Warning 'Install-WindowsFeature not available. Ensure IIS is installed manually.'
    }

    if (-not (Test-IisModuleAvailable)) {
        throw 'WebAdministration module not found. Ensure IIS is installed.'
    }

    Import-Module WebAdministration

    # ARR availability is checked separately via Test-ArrInstalled; nothing to validate here.
}

function Set-JsonValue {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string[]]$Path,
        [Parameter(Mandatory = $true)][object]$Value
    )

    $cursor = $Object
    for ($i = 0; $i -lt $Path.Length - 1; $i++) {
        $segment = $Path[$i]
        if ($null -eq $cursor.$segment) {
            $cursor | Add-Member -NotePropertyName $segment -NotePropertyValue ([pscustomobject]@{})
        }
        $cursor = $cursor.$segment
    }

    $leaf = $Path[$Path.Length - 1]
    if ($null -eq $cursor.PSObject.Properties[$leaf]) {
        $cursor | Add-Member -NotePropertyName $leaf -NotePropertyValue $Value
    }
    else {
        $cursor.$leaf = $Value
    }
}

Ensure-Admin

if ($CreateSelfSignedCert) {
    Write-Step 'Creating self-signed TLS certificate'
    $CertificateThumbprint = New-DeploymentCertificate -DnsName $HostName
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'BeaconRelay.LpdReceiver\BeaconRelay.LpdReceiver.csproj'
$publishPath = Join-Path $AppRoot 'publish'
$dbDir = Split-Path -Parent $DatabasePath
$proxyRoot = Join-Path $AppRoot 'iis-proxy'

Write-Step 'Creating deployment directories'
Ensure-Directory -Path $AppRoot
Ensure-Directory -Path $publishPath
Ensure-Directory -Path $dbDir
Ensure-Directory -Path $InboxPath
Ensure-Directory -Path $RoutedPath
Ensure-Directory -Path $proxyRoot

if (-not $NoPublish) {
    Write-Step 'Publishing application'
    $publishArgs = @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-o', $publishPath
    )

    if ($PSCmdlet.ShouldProcess($projectPath, 'dotnet publish')) {
        & dotnet @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw 'dotnet publish failed.'
        }
    }
}

Write-Step 'Applying production appsettings'
$appSettingsPath = Join-Path $publishPath 'appsettings.json'
if (-not (Test-Path -LiteralPath $appSettingsPath)) {
    throw "Could not find $appSettingsPath"
}

$appSettings = Get-Content -Raw -LiteralPath $appSettingsPath | ConvertFrom-Json
Set-JsonValue -Object $appSettings -Path @('Health', 'Port') -Value $KestrelPort
Set-JsonValue -Object $appSettings -Path @('AdminAuth', 'Enabled') -Value $true
Set-JsonValue -Object $appSettings -Path @('AdminAuth', 'Username') -Value $AdminUsername
Set-JsonValue -Object $appSettings -Path @('AdminAuth', 'Password') -Value $AdminPassword
Set-JsonValue -Object $appSettings -Path @('Database', 'ConnectionString') -Value ("Data Source={0}" -f $DatabasePath)
Set-JsonValue -Object $appSettings -Path @('Storage', 'OutputDirectory') -Value $InboxPath

if ($PSCmdlet.ShouldProcess($appSettingsPath, 'Write appsettings.json')) {
    $json = $appSettings | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $appSettingsPath -Value $json -Encoding UTF8
}

Write-Step 'Creating/updating Windows service'
$exePath = Join-Path $publishPath 'BeaconRelay.LpdReceiver.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Service executable not found: $exePath"
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -ne 'Stopped') {
        if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop existing service')) {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
        }
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, 'Delete existing service')) {
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 1
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, 'Create service')) {
    & sc.exe create $ServiceName binPath= ('"' + $exePath + '"') start= auto | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($ServiceUser) -and -not [string]::IsNullOrWhiteSpace($ServicePassword)) {
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Configure service credentials')) {
        & sc.exe config $ServiceName obj= $ServiceUser password= $ServicePassword | Out-Null
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, 'Configure service recovery')) {
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
}

if ($PSCmdlet.ShouldProcess($ServiceName, 'Start service')) {
    & sc.exe start $ServiceName | Out-Null
}

if (-not $SkipIis) {
    Write-Step 'Configuring IIS reverse proxy'
    Ensure-IisFeatures

    $arrAvailable = Test-ArrInstalled
    if (-not $arrAvailable -and $AutoInstallIisProxyModules) {
        Write-Step 'Installing IIS proxy modules (URL Rewrite + ARR)'
        $arrAvailable = Ensure-IisProxyModules
    }

    # Enable ARR proxy (only if ARR is installed)
    if ($arrAvailable) {
        if ($PSCmdlet.ShouldProcess('IIS', 'Enable ARR proxy')) {
            Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name 'enabled' -Value 'True'
            Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/proxy' -Name 'preserveHostHeader' -Value 'True'
        }
    }
    else {
        Write-Warning @"
IIS Application Request Routing (ARR) is not installed. The IIS site will be created
but reverse-proxy forwarding to Kestrel will NOT work until ARR is installed.

Install both modules (in this order) from IIS.NET, then re-run the script:
  1. URL Rewrite 2.1  https://www.iis.net/downloads/microsoft/url-rewrite
  2. ARR 3.0          https://www.iis.net/downloads/microsoft/application-request-routing

Tip: you can ask this script to auto-install them with -AutoInstallIisProxyModules.
"@
    }

    $proxyWebConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="BeaconRelayReverseProxy" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite" url="http://127.0.0.1:$KestrelPort/{R:1}" appendQueryString="true" />
        </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
"@

    if ($PSCmdlet.ShouldProcess((Join-Path $proxyRoot 'web.config'), 'Write IIS reverse proxy web.config')) {
        Set-Content -LiteralPath (Join-Path $proxyRoot 'web.config') -Value $proxyWebConfig -Encoding UTF8
    }

    if (-not (Test-Path "IIS:\AppPools\$SiteName")) {
        if ($PSCmdlet.ShouldProcess($SiteName, 'Create IIS app pool')) {
            New-WebAppPool -Name $SiteName | Out-Null
        }
    }

    if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
        if ($PSCmdlet.ShouldProcess($SiteName, 'Create IIS site')) {
            New-Website -Name $SiteName -PhysicalPath $proxyRoot -Port 80 -HostHeader $HostName -ApplicationPool $SiteName | Out-Null
        }
    }
    else {
        if ($PSCmdlet.ShouldProcess($SiteName, 'Update IIS site path and app pool')) {
            Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $proxyRoot
            Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $existingHttpsBinding = Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -like "*:443:$HostName" }
        if (-not $existingHttpsBinding) {
            if ($PSCmdlet.ShouldProcess($SiteName, 'Add HTTPS binding')) {
                New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName | Out-Null
            }
        }

        $certPath = "cert:\LocalMachine\My\$CertificateThumbprint"
        if (-not (Test-Path $certPath)) {
            throw "Certificate with thumbprint $CertificateThumbprint not found in LocalMachine\\My."
        }

        if ($PSCmdlet.ShouldProcess($SiteName, 'Assign TLS certificate')) {
            $bindingPath = "IIS:\SslBindings\0.0.0.0!443!$HostName"
            if (Test-Path $bindingPath) {
                Remove-Item $bindingPath -Force
            }
            New-Item $bindingPath -Thumbprint $CertificateThumbprint -SSLFlags 1 | Out-Null
        }
    }
    else {
        Write-Warning 'No CertificateThumbprint supplied. IIS site is configured on HTTP only.'
    }
}

if (-not $SkipFirewall) {
    Write-Step 'Configuring firewall rules'

    if (-not (Get-NetFirewallRule -DisplayName "$ServiceName LPD Inbound" -ErrorAction SilentlyContinue)) {
        if ($PSCmdlet.ShouldProcess('Windows Firewall', 'Create LPD inbound rule')) {
            New-NetFirewallRule -DisplayName "$ServiceName LPD Inbound" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $LpdPort -RemoteAddress $AllowedLpdRemoteAddresses | Out-Null
        }
    }

    if (-not (Get-NetFirewallRule -DisplayName "$ServiceName HTTPS Inbound" -ErrorAction SilentlyContinue)) {
        if ($PSCmdlet.ShouldProcess('Windows Firewall', 'Create HTTPS inbound rule')) {
            New-NetFirewallRule -DisplayName "$ServiceName HTTPS Inbound" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 443 | Out-Null
        }
    }
}

Write-Step 'Deployment completed'
Write-Host "Service Name: $ServiceName"
Write-Host "Site Name:    $SiteName"
Write-Host "Host Name:    $HostName"
Write-Host "Kestrel Port: $KestrelPort"
Write-Host "LPD Port:     $LpdPort"
Write-Host "App Root:     $AppRoot"
Write-Host ''
Write-Host 'Quick checks:' -ForegroundColor Yellow
Write-Host "1) sc.exe query $ServiceName"
Write-Host "2) Browse: https://$HostName/admin/index.html"
Write-Host "3) Health:  https://$HostName/healthz"
