[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ProjectPath = '',
    [string]$ArtifactRoot = '',
    [string]$ArtifactName = '',
    [switch]$SelfContained,
    [switch]$SkipZip,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-ShortCommit {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        return ''
    }

    try {
        $sha = (& git rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($sha)) {
            return $sha.Trim()
        }
    }
    catch {
    }

    return ''
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot 'BeaconRelay.LpdReceiver\BeaconRelay.LpdReceiver.csproj'
}
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $repoRoot 'artifacts'
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}

$selfContainedValue = if ($SelfContained.IsPresent) { 'true' } else { 'false' }
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$shortCommit = Get-ShortCommit

if ([string]::IsNullOrWhiteSpace($ArtifactName)) {
    $nameParts = @('BeaconRelay', $Configuration, $Runtime, $timestamp)
    if (-not [string]::IsNullOrWhiteSpace($shortCommit)) {
        $nameParts += $shortCommit
    }
    $ArtifactName = ($nameParts -join '-')
}

$artifactDir = Join-Path $ArtifactRoot $ArtifactName
$publishDir = Join-Path $artifactDir 'publish'
$deployDir = Join-Path $artifactDir 'deploy'
$zipPath = Join-Path $ArtifactRoot ($ArtifactName + '.zip')

Write-Step 'Preparing artifact workspace'
Ensure-Directory -Path $ArtifactRoot
if (Test-Path -LiteralPath $artifactDir) {
    if (-not $Clean) {
        throw "Artifact directory already exists: $artifactDir. Use -Clean or provide a different -ArtifactName."
    }

    if ($PSCmdlet.ShouldProcess($artifactDir, 'Remove existing artifact directory')) {
        Remove-Item -LiteralPath $artifactDir -Recurse -Force
    }
}

Ensure-Directory -Path $artifactDir
Ensure-Directory -Path $publishDir
Ensure-Directory -Path $deployDir

Write-Step 'Publishing application for deployment artifact'
$publishArgs = @(
    'publish',
    $ProjectPath,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $selfContainedValue,
    '-o', $publishDir
)

if ($PSCmdlet.ShouldProcess($ProjectPath, 'dotnet publish for artifact')) {
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed while creating artifact.'
    }
}

Write-Step 'Copying deployment scripts'
$deployScriptSource = Join-Path $repoRoot 'deploy'
Get-ChildItem -LiteralPath $deployScriptSource -File -Filter '*.ps1' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $deployDir $_.Name) -Force
}

Write-Step 'Generating SHA-256 checksums'
$checksumPath = Join-Path $artifactDir 'checksums.sha256'
$hashLines = Get-ChildItem -LiteralPath $artifactDir -File -Recurse |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($artifactDir, $_.FullName)
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "{0} *{1}" -f $hash, $relative.Replace('\\', '/')
    }
Set-Content -LiteralPath $checksumPath -Value $hashLines -Encoding UTF8

if (-not $SkipZip) {
    Write-Step 'Creating compressed artifact archive'
    if (Test-Path -LiteralPath $zipPath) {
        if ($PSCmdlet.ShouldProcess($zipPath, 'Remove existing artifact archive')) {
            Remove-Item -LiteralPath $zipPath -Force
        }
    }

    if ($PSCmdlet.ShouldProcess($zipPath, 'Create artifact zip')) {
        Compress-Archive -Path (Join-Path $artifactDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    }
}

Write-Step 'Artifact creation complete'
Write-Host "Artifact Directory: $artifactDir"
if (-not $SkipZip) {
    Write-Host "Artifact Zip:       $zipPath"
}
Write-Host ""
Write-Host 'Deploy on target machine with:' -ForegroundColor Yellow
Write-Host '  1) Copy artifact contents so publish/ and deploy/ are under your AppRoot'
Write-Host '  2) Run deploy/Deploy-BeaconRelay.ps1 -Mode Deploy -NoPublish [other options]'
