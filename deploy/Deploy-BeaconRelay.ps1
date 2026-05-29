[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Deploy', 'Upgrade', 'Remove', 'Status')]
    [string]$Mode = 'Deploy',
    [string]$ServiceName = 'Interbit Beacon Relay',
    [string]$ServiceDescription = 'Receives LPD print jobs, applies routing rules, and hosts the Beacon Relay admin interface.',
    [string]$SiteName = 'BeaconRelay Admin',
    [string]$HostName = 'beaconrelay.local',
    [int]$KestrelPort = 8080,
    [int]$LpdPort = 515,
    [string]$AppRoot = 'C:\Program Files\Interbit\Beacon Relay',
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$AdminUsername = 'admin',
    [string]$AdminPassword = 'change-me-now',
    [string]$DatabasePassword = '',
    [string]$DatabasePath = 'C:\ProgramData\Interbit\Beacon Relay\db\beacon-relay.db',
    [string]$InboxPath = 'C:\ProgramData\Interbit\Beacon Relay\data\inbox',
    [string]$RoutedPath = 'C:\ProgramData\Interbit\Beacon Relay\data\routed',
    [string]$ServiceUser = '',
    [string]$ServicePassword = '',
    [string]$CertificateThumbprint = '',
    [string]$AllowedLpdRemoteAddresses = 'LocalSubnet',
    [switch]$AutoInstallIisProxyModules,
    [switch]$CreateSelfSignedCert,
    [switch]$StatusAsJson,
    [switch]$PurgeAppRoot,
    [switch]$SkipIis,
    [switch]$SkipFirewall,
    [switch]$ConvertExistingDatabaseToSqlCipher,
    [string]$DatabaseBackupPath = '',
    [switch]$SkipDatabaseConversionBackup,
    [switch]$DatabaseConversionWorker,
    [switch]$NoPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($SkipDatabaseConversionBackup -and -not [string]::IsNullOrWhiteSpace($DatabaseBackupPath)) {
    throw 'Specify either -SkipDatabaseConversionBackup or -DatabaseBackupPath, not both.'
}

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

function Remove-ExistingService {
    param([string]$Name)
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) {
        return
    }

    if ($svc.Status -ne 'Stopped') {
        if ($PSCmdlet.ShouldProcess($Name, 'Stop existing service')) {
            Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
        }
    }

    if ($PSCmdlet.ShouldProcess($Name, 'Delete existing service')) {
        & sc.exe delete $Name | Out-Null
    }
}

function Stop-ExistingService {
    param([string]$Name)
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) {
        return
    }

    if ($svc.Status -ne 'Stopped') {
        if ($PSCmdlet.ShouldProcess($Name, 'Stop existing service')) {
            Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
        }
    }
}

function Import-SqlCipherAssemblies {
    param([string]$PublishRoot)

    $requiredAssemblies = @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlcipher.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll'
    )

    foreach ($assemblyName in $requiredAssemblies) {
        $assemblyPath = Join-Path $PublishRoot $assemblyName
        if (-not (Test-Path -LiteralPath $assemblyPath)) {
            throw "Required SQLCipher assembly not found in publish output: $assemblyPath"
        }

        [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
    }

    [SQLitePCL.Batteries_V2]::Init()
}

function Test-SqliteOpen {
    param(
        [string]$DatabaseFile,
        [string]$Password = ''
    )

    if (-not (Test-Path -LiteralPath $DatabaseFile)) {
        return $false
    }

    $connectionStringBuilder = [Microsoft.Data.Sqlite.SqliteConnectionStringBuilder]::new()
    $connectionStringBuilder.DataSource = $DatabaseFile
    $connectionStringBuilder.Mode = [Microsoft.Data.Sqlite.SqliteOpenMode]::ReadWrite
    if (-not [string]::IsNullOrWhiteSpace($Password)) {
        $connectionStringBuilder.Password = $Password
    }

    $connection = $null
    try {
        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($connectionStringBuilder.ToString())
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = 'SELECT COUNT(*) FROM sqlite_master;'
        [void]$command.ExecuteScalar()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }
}

function Convert-ToSqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

function Invoke-DatabaseSqlCipherConversion {
    param(
        [string]$PublishRoot,
        [string]$DatabaseFile,
        [string]$Password,
        [string]$BackupPath = '',
        [bool]$SkipBackup = $false
    )

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw 'DatabasePassword is required when converting an existing database to SQLCipher.'
    }

    if (-not (Test-Path -LiteralPath $DatabaseFile)) {
        Write-Host '  Database file not found. Skipping SQLCipher conversion.' -ForegroundColor DarkGray
        return
    }

    Import-SqlCipherAssemblies -PublishRoot $PublishRoot

    if (Test-SqliteOpen -DatabaseFile $DatabaseFile -Password $Password) {
        Write-Host '  Database already opens with the configured SQLCipher password. Skipping conversion.' -ForegroundColor DarkGray
        return
    }

    if (-not (Test-SqliteOpen -DatabaseFile $DatabaseFile)) {
        throw 'Existing database is not readable as plaintext SQLite, and it did not open with the provided SQLCipher password. Aborting conversion.'
    }

    $timestamp = Get-Date -Format 'yyyyMMddHHmmss'
    $temporaryEncryptedPath = "$DatabaseFile.sqlcipher-$timestamp.tmp"
    if ([string]::IsNullOrWhiteSpace($BackupPath)) {
        $backupPath = "$DatabaseFile.pre-sqlcipher-$timestamp.bak"
    }
    else {
        $backupPath = $BackupPath
    }
    $walPath = "$DatabaseFile-wal"
    $shmPath = "$DatabaseFile-shm"
    $backupWalPath = "$backupPath-wal"
    $backupShmPath = "$backupPath-shm"

    $sourceBuilder = [Microsoft.Data.Sqlite.SqliteConnectionStringBuilder]::new()
    $sourceBuilder.DataSource = $DatabaseFile
    $sourceBuilder.Mode = [Microsoft.Data.Sqlite.SqliteOpenMode]::ReadWrite

    $connection = $null
    try {
        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($sourceBuilder.ToString())
        $connection.Open()

        $sqlStatements = @(
            'PRAGMA busy_timeout = 5000;',
            'PRAGMA wal_checkpoint(FULL);',
            ('ATTACH DATABASE ''{0}'' AS encrypted KEY ''{1}'';' -f (Convert-ToSqlLiteral $temporaryEncryptedPath), (Convert-ToSqlLiteral $Password)),
            'SELECT sqlcipher_export(''encrypted'');',
            'DETACH DATABASE encrypted;'
        )

        foreach ($statement in $sqlStatements) {
            $command = $connection.CreateCommand()
            $command.CommandText = $statement
            [void]$command.ExecuteNonQuery()
            $command.Dispose()
        }
    }
    finally {
        if ($null -ne $connection) {
            $connection.Dispose()
        }
    }

    if (-not (Test-Path -LiteralPath $temporaryEncryptedPath)) {
        throw 'SQLCipher conversion did not produce an encrypted database file.'
    }

    if (-not $SkipBackup) {
        $backupDirectory = Split-Path -Parent $backupPath
        if (-not [string]::IsNullOrWhiteSpace($backupDirectory)) {
            Ensure-Directory -Path $backupDirectory
        }

        if ($PSCmdlet.ShouldProcess($DatabaseFile, 'Backup plaintext SQLite database before SQLCipher replacement')) {
            Copy-Item -LiteralPath $DatabaseFile -Destination $backupPath -Force
            if (Test-Path -LiteralPath $walPath) {
                Copy-Item -LiteralPath $walPath -Destination $backupWalPath -Force
            }
            if (Test-Path -LiteralPath $shmPath) {
                Copy-Item -LiteralPath $shmPath -Destination $backupShmPath -Force
            }
        }
    }

    if ($PSCmdlet.ShouldProcess($DatabaseFile, 'Replace plaintext SQLite database with SQLCipher-encrypted database')) {
        Move-Item -LiteralPath $temporaryEncryptedPath -Destination $DatabaseFile -Force
        if (Test-Path -LiteralPath $walPath) {
            Remove-Item -LiteralPath $walPath -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $shmPath) {
            Remove-Item -LiteralPath $shmPath -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-SqliteOpen -DatabaseFile $DatabaseFile -Password $Password)) {
        if ($SkipBackup) {
            throw 'SQLCipher verification failed after conversion, and backup creation was skipped.'
        }

        throw "SQLCipher verification failed after conversion. Original database backup retained at $backupPath"
    }

    if ($SkipBackup) {
        Write-Host '  SQLCipher conversion completed. Backup creation was skipped by request.' -ForegroundColor DarkGray
    }
    else {
        Write-Host "  SQLCipher conversion completed. Plaintext backup: $backupPath" -ForegroundColor DarkGray
    }
}

function Remove-IisSiteAndPool {
    param(
        [string]$Site,
        [string]$Pool,
        [string]$HostName
    )

    if (-not (Test-IisModuleAvailable)) {
        return
    }

    Import-Module WebAdministration

    if (Get-Website -Name $Site -ErrorAction SilentlyContinue) {
        if ($PSCmdlet.ShouldProcess($Site, 'Remove IIS website')) {
            Remove-Website -Name $Site
        }
    }

    if (Test-Path "IIS:\AppPools\$Pool") {
        if ($PSCmdlet.ShouldProcess($Pool, 'Remove IIS app pool')) {
            Remove-WebAppPool -Name $Pool
        }
    }

    # Best-effort cleanup for host-specific SSL mapping if it still exists.
    foreach ($path in @("IIS:\SslBindings\0.0.0.0!443!$HostName")) {
        if (Test-Path $path) {
            if ($PSCmdlet.ShouldProcess($path, 'Remove IIS SSL binding mapping')) {
                Remove-Item $path -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Remove-FirewallRules {
    param([string]$Name)

    foreach ($ruleName in @("$Name LPD Inbound", "$Name HTTPS Inbound")) {
        if (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue) {
            if ($PSCmdlet.ShouldProcess($ruleName, 'Remove firewall rule')) {
                Remove-NetFirewallRule -DisplayName $ruleName | Out-Null
            }
        }
    }
}

function Show-DeploymentStatus {
    param(
        [string]$SvcName,
        [string]$Site,
        [string]$Pool,
        [string]$HostName,
        [string]$Root,
        [string]$Thumbprint,
        [switch]$AsJson
    )

    $status = [ordered]@{}

    $svc = Get-Service -Name $SvcName -ErrorAction SilentlyContinue
    if ($svc) {
        $status.Service = [ordered]@{
            Name = $SvcName
            Exists = $true
            State = [string]$svc.Status
        }
    }
    else {
        $status.Service = [ordered]@{
            Name = $SvcName
            Exists = $false
            State = 'NotFound'
        }
    }

    if (Test-IisModuleAvailable) {
        Import-Module WebAdministration

        $siteObj = Get-Website -Name $Site -ErrorAction SilentlyContinue
        if ($siteObj) {
            $siteStatus = [ordered]@{
                Name = $Site
                Exists = $true
                State = [string]$siteObj.State
            }
        }
        else {
            $siteStatus = [ordered]@{
                Name = $Site
                Exists = $false
                State = 'NotFound'
            }
        }

        if (Test-Path "IIS:\AppPools\$Pool") {
            $poolState = (Get-WebAppPoolState -Name $Pool).Value
            $poolStatus = [ordered]@{
                Name = $Pool
                Exists = $true
                State = [string]$poolState
            }
        }
        else {
            $poolStatus = [ordered]@{
                Name = $Pool
                Exists = $false
                State = 'NotFound'
            }
        }

        $httpsBinding = Get-WebBinding -Name $Site -Protocol https -Port 443 -HostHeader $HostName -ErrorAction SilentlyContinue
        if ($httpsBinding) {
            $httpsStatus = [ordered]@{
                Host = $HostName
                Exists = $true
            }
        }
        else {
            $httpsStatus = [ordered]@{
                Host = $HostName
                Exists = $false
            }
        }

        $status.Iis = [ordered]@{
            ModuleAvailable = $true
            Site = $siteStatus
            AppPool = $poolStatus
            HttpsBinding = $httpsStatus
            ArrInstalled = [bool](Test-ArrInstalled)
        }
    }
    else {
        $status.Iis = [ordered]@{
            ModuleAvailable = $false
            Site = $null
            AppPool = $null
            HttpsBinding = $null
            ArrInstalled = [bool](Test-ArrInstalled)
        }
    }

    $firewall = @()
    foreach ($ruleName in @("$SvcName LPD Inbound", "$SvcName HTTPS Inbound")) {
        $rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
        if ($rule) {
            $firewall += [ordered]@{
                Name = $ruleName
                Exists = $true
                Enabled = [string]$rule.Enabled
            }
        }
        else {
            $firewall += [ordered]@{
                Name = $ruleName
                Exists = $false
                Enabled = 'NotFound'
            }
        }
    }
    $status.FirewallRules = $firewall

    if (Test-Path -LiteralPath $Root) {
        $appRootExists = $true
    }
    else {
        $appRootExists = $false
    }

    $publishExe = Join-Path $Root 'publish\BeaconRelay.LpdReceiver.exe'
    if (Test-Path -LiteralPath $publishExe) {
        $publishedExeExists = $true
    }
    else {
        $publishedExeExists = $false
    }

    $status.Paths = [ordered]@{
        AppRoot = $Root
        AppRootExists = $appRootExists
        PublishedExe = $publishExe
        PublishedExeExists = $publishedExeExists
    }

    if (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
        $certPath = "cert:\LocalMachine\My\$Thumbprint"
        if (Test-Path $certPath) {
            $cert = Get-Item $certPath
            $status.Certificate = [ordered]@{
                Thumbprint = $Thumbprint
                Found = $true
                Subject = $cert.Subject
                NotAfterUtc = $cert.NotAfter.ToString('u')
            }
        }
        else {
            $status.Certificate = [ordered]@{
                Thumbprint = $Thumbprint
                Found = $false
                Subject = $null
                NotAfterUtc = $null
            }
        }
    }
    else {
        $status.Certificate = $null
    }

    if ($AsJson) {
        $status | ConvertTo-Json -Depth 8
        return
    }

    Write-Step 'Current deployment status'
    Write-Host ("Service:     {0} ({1})" -f $status.Service.Name, $status.Service.State)

    if ($status.Iis.ModuleAvailable) {
        Write-Host ("IIS Site:    {0} ({1})" -f $status.Iis.Site.Name, $status.Iis.Site.State)
        Write-Host ("App Pool:    {0} ({1})" -f $status.Iis.AppPool.Name, $status.Iis.AppPool.State)
        if ($status.Iis.HttpsBinding.Exists) {
            Write-Host ("HTTPS Bind:  present for host {0}" -f $status.Iis.HttpsBinding.Host)
        }
        else {
            Write-Host ("HTTPS Bind:  not found for host {0}" -f $status.Iis.HttpsBinding.Host)
        }
        Write-Host ("ARR Module:  {0}" -f $(if ($status.Iis.ArrInstalled) { 'Installed' } else { 'Missing' }))
    }
    else {
        Write-Host 'IIS:         WebAdministration module unavailable'
    }

    foreach ($rule in $status.FirewallRules) {
        if ($rule.Exists) {
            Write-Host ("Firewall:    {0} ({1})" -f $rule.Name, $rule.Enabled)
        }
        else {
            Write-Host ("Firewall:    {0} (not found)" -f $rule.Name)
        }
    }

    Write-Host ("App Root:    {0} ({1})" -f $status.Paths.AppRoot, $(if ($status.Paths.AppRootExists) { 'exists' } else { 'missing' }))
    Write-Host ("Published EXE: {0} ({1})" -f $status.Paths.PublishedExe, $(if ($status.Paths.PublishedExeExists) { 'present' } else { 'missing' }))

    if ($status.Certificate) {
        if ($status.Certificate.Found) {
            Write-Host ("Cert:        found ({0}) expires {1}" -f $status.Certificate.Thumbprint, $status.Certificate.NotAfterUtc)
        }
        else {
            Write-Host ("Cert:        thumbprint not found in LocalMachine\\My ({0})" -f $status.Certificate.Thumbprint)
        }
    }
}

Ensure-Admin

if ($DatabaseConversionWorker) {
    Write-Step 'Running SQLCipher conversion worker'
    $workerPublishPath = Join-Path $AppRoot 'publish'
    Invoke-DatabaseSqlCipherConversion -PublishRoot $workerPublishPath -DatabaseFile $DatabasePath -Password $DatabasePassword -BackupPath $DatabaseBackupPath -SkipBackup:$SkipDatabaseConversionBackup
    exit 0
}

if ($Mode -eq 'Status') {
    Show-DeploymentStatus -SvcName $ServiceName -Site $SiteName -Pool $SiteName -HostName $HostName -Root $AppRoot -Thumbprint $CertificateThumbprint -AsJson:$StatusAsJson
    exit 0
}

if ($Mode -eq 'Remove') {
    Write-Step 'Removing Windows service'
    Remove-ExistingService -Name $ServiceName

    if (-not $SkipIis) {
        Write-Step 'Removing IIS website and app pool'
        Remove-IisSiteAndPool -Site $SiteName -Pool $SiteName -HostName $HostName
    }

    if (-not $SkipFirewall) {
        Write-Step 'Removing firewall rules'
        Remove-FirewallRules -Name $ServiceName
    }

    if ($PurgeAppRoot -and (Test-Path -LiteralPath $AppRoot)) {
        $resolvedAppRoot = [System.IO.Path]::GetFullPath($AppRoot).TrimEnd('\\')
        $resolvedCurrent = [System.IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('\\')
        if ($resolvedCurrent.StartsWith($resolvedAppRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Cannot use -PurgeAppRoot while current directory is inside AppRoot ($resolvedCurrent). Run the remove command from outside $resolvedAppRoot or omit -PurgeAppRoot."
        }

        if ($PSCmdlet.ShouldProcess($AppRoot, 'Delete application root directory')) {
            Remove-Item -LiteralPath $AppRoot -Recurse -Force
        }
    }

    Write-Step 'Removal completed'
    Write-Host "Service Name: $ServiceName"
    Write-Host "Site Name:    $SiteName"
    Write-Host "App Root:     $AppRoot"
    exit 0
}

$shouldAutoCreateSelfSignedCert = (-not $SkipIis) -and ($Mode -eq 'Deploy' -or $Mode -eq 'Upgrade') -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)

if ($shouldAutoCreateSelfSignedCert -or $CreateSelfSignedCert) {
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
# AdminAuth now supplies bootstrap credentials for first-run admin user creation.
Set-JsonValue -Object $appSettings -Path @('AdminAuth', 'Enabled') -Value $false
Set-JsonValue -Object $appSettings -Path @('AdminAuth', 'Username') -Value $AdminUsername
Set-JsonValue -Object $appSettings -Path @('AdminAuth', 'Password') -Value $AdminPassword
Set-JsonValue -Object $appSettings -Path @('Database', 'ConnectionString') -Value ("Data Source={0}" -f $DatabasePath)
Set-JsonValue -Object $appSettings -Path @('Database', 'Password') -Value $DatabasePassword
Set-JsonValue -Object $appSettings -Path @('Storage', 'OutputDirectory') -Value $InboxPath

if ($PSCmdlet.ShouldProcess($appSettingsPath, 'Write appsettings.json')) {
    $json = $appSettings | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $appSettingsPath -Value $json -Encoding UTF8
}

if ($ConvertExistingDatabaseToSqlCipher) {
    Write-Step 'Converting existing SQLite database to SQLCipher'
    Stop-ExistingService -Name $ServiceName

    $isPowerShellCore = $PSVersionTable.PSEdition -eq 'Core' -and $PSVersionTable.PSVersion.Major -ge 7
    if ($isPowerShellCore) {
        Invoke-DatabaseSqlCipherConversion -PublishRoot $publishPath -DatabaseFile $DatabasePath -Password $DatabasePassword -BackupPath $DatabaseBackupPath -SkipBackup:$SkipDatabaseConversionBackup
    }
    else {
        $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
        if (-not $pwsh) {
            throw @"
SQLCipher conversion requires PowerShell 7+ (pwsh) because the conversion logic loads .NET runtime assemblies from publish output.

Install PowerShell 7 and re-run this command, or run deployment without -ConvertExistingDatabaseToSqlCipher.
"@
        }

        $workerArgs = @(
            '-ExecutionPolicy', 'Bypass',
            '-File', $PSCommandPath,
            '-Mode', $Mode,
            '-ServiceName', $ServiceName,
            '-ServiceDescription', $ServiceDescription,
            '-SiteName', $SiteName,
            '-HostName', $HostName,
            '-KestrelPort', $KestrelPort,
            '-LpdPort', $LpdPort,
            '-AppRoot', $AppRoot,
            '-Runtime', $Runtime,
            '-Configuration', $Configuration,
            '-AdminUsername', $AdminUsername,
            '-AdminPassword', $AdminPassword,
            '-DatabasePassword', $DatabasePassword,
            '-DatabasePath', $DatabasePath,
            '-InboxPath', $InboxPath,
            '-RoutedPath', $RoutedPath,
            '-ServiceUser', $ServiceUser,
            '-ServicePassword', $ServicePassword,
            '-CertificateThumbprint', $CertificateThumbprint,
            '-AllowedLpdRemoteAddresses', $AllowedLpdRemoteAddresses,
            '-DatabaseBackupPath', $DatabaseBackupPath,
            '-DatabaseConversionWorker'
        )

        if ($SkipDatabaseConversionBackup) {
            $workerArgs += '-SkipDatabaseConversionBackup'
        }

        if ($NoPublish) {
            $workerArgs += '-NoPublish'
        }

        if ($PSCmdlet.ShouldProcess($DatabasePath, 'Run SQLCipher conversion in pwsh worker')) {
            & $pwsh.Source @workerArgs
            if ($LASTEXITCODE -ne 0) {
                throw 'SQLCipher conversion worker failed.'
            }
        }
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($DatabasePassword) -and (Test-Path -LiteralPath $DatabasePath)) {
    Write-Warning 'DatabasePassword is set and a database already exists, but -ConvertExistingDatabaseToSqlCipher was not specified. Existing plaintext databases are not converted automatically.'
}

if ($Mode -eq 'Upgrade') {
    Write-Step 'Upgrading Windows service'
}
else {
    Write-Step 'Creating/updating Windows service'
}
$exePath = Join-Path $publishPath 'BeaconRelay.LpdReceiver.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Service executable not found: $exePath"
}

Remove-ExistingService -Name $ServiceName

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

if ($PSCmdlet.ShouldProcess($ServiceName, 'Set service description')) {
    & sc.exe description $ServiceName $ServiceDescription | Out-Null
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
        $certPath = "cert:\LocalMachine\My\$CertificateThumbprint"
        if (-not (Test-Path $certPath)) {
            throw "Certificate with thumbprint $CertificateThumbprint not found in LocalMachine\\My."
        }

        $bindingMatch = "*:443:$HostName"
        $httpsBindings = @(Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -eq $bindingMatch })

        if ($httpsBindings.Count -gt 1) {
            if ($PSCmdlet.ShouldProcess($SiteName, 'Remove duplicate HTTPS bindings')) {
                $httpsBindings | Select-Object -Skip 1 | ForEach-Object {
                    Remove-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName
                }
            }
            $httpsBindings = @(Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -eq $bindingMatch })
        }

        if ($httpsBindings.Count -eq 0) {
            if ($PSCmdlet.ShouldProcess($SiteName, 'Add HTTPS binding with SNI')) {
                New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1 | Out-Null
            }
        }

        if ($PSCmdlet.ShouldProcess($SiteName, 'Assign TLS certificate')) {
            $binding = Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -ErrorAction SilentlyContinue
            if (-not $binding) {
                throw "HTTPS binding for host '$HostName' was not found after creation attempt."
            }

            try {
                $binding.AddSslCertificate($CertificateThumbprint, 'My')
            }
            catch {
                # Recreate the binding once and retry certificate assignment.
                Remove-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -ErrorAction SilentlyContinue
                New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1 | Out-Null
                $binding = Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -ErrorAction Stop
                $binding.AddSslCertificate($CertificateThumbprint, 'My')
            }
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
if ([string]::IsNullOrWhiteSpace($DatabasePassword)) {
    Write-Warning 'DatabasePassword was not set. The deployed database will not use SQLCipher encryption.'
}
Write-Host ''
Write-Host 'Quick checks:' -ForegroundColor Yellow
Write-Host "1) sc.exe query $ServiceName"
Write-Host "2) Browse: https://$HostName/admin/index.html"
Write-Host "3) Health:  https://$HostName/healthz"
