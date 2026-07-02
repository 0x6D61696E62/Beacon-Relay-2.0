[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ProjectPath = '',
    [string]$ArtifactRoot = '',
    [string]$ArtifactName = '',
    [string]$VersionPrefix = '',
    [string]$BuildNumber = '',
    [string]$SourceRevisionId = '',
    [switch]$GenerateReleaseNotes,
    [string]$ReleaseNotesOutputPath = '',
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

function Get-RelativePathCompat {
    param(
        [string]$BasePath,
        [string]$ChildPath
    )

    $resolvedBase = (Resolve-Path -LiteralPath $BasePath).Path
    $resolvedChild = (Resolve-Path -LiteralPath $ChildPath).Path

    if (-not $resolvedBase.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        $resolvedBase = $resolvedBase + [IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [Uri]$resolvedBase
    $childUri = [Uri]$resolvedChild
    $relativeUri = $baseUri.MakeRelativeUri($childUri)
    $relativePath = [Uri]::UnescapeDataString($relativeUri.ToString())
    return $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
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

function Get-VersionPrefixFromProps {
    param([string]$RepositoryRoot)

    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    if (-not (Test-Path -LiteralPath $propsPath)) {
        return ''
    }

    try {
        [xml]$xml = Get-Content -LiteralPath $propsPath -Raw
        $value = $xml.Project.PropertyGroup.VersionPrefix | Select-Object -First 1
        $text = if ($value -is [System.Xml.XmlNode]) { $value.InnerText } else { [string]$value }
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            return $text
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

if ([string]::IsNullOrWhiteSpace($VersionPrefix)) {
    $VersionPrefix = Get-VersionPrefixFromProps -RepositoryRoot $repoRoot
}
if ([string]::IsNullOrWhiteSpace($VersionPrefix)) {
    $VersionPrefix = '2.0.0'
}

if ([string]::IsNullOrWhiteSpace($BuildNumber)) {
    $BuildNumber = Get-Date -Format 'yyyyMMddHHmmss'
}

if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    $SourceRevisionId = if (-not [string]::IsNullOrWhiteSpace($shortCommit)) { $shortCommit } else { 'local' }
}

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
$restoreArgs = @(
    'restore',
    $ProjectPath,
    '-r', $Runtime,
    ('-p:VersionPrefix={0}' -f $VersionPrefix),
    ('-p:BuildNumber={0}' -f $BuildNumber),
    ('-p:SourceRevisionId={0}' -f $SourceRevisionId)
)

if ($PSCmdlet.ShouldProcess($ProjectPath, 'dotnet restore for target runtime')) {
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet restore failed while creating artifact.'
    }
}

$publishArgs = @(
    'publish',
    $ProjectPath,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', $selfContainedValue,
    ('-p:VersionPrefix={0}' -f $VersionPrefix),
    ('-p:BuildNumber={0}' -f $BuildNumber),
    ('-p:SourceRevisionId={0}' -f $SourceRevisionId),
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
if (-not (Test-Path -LiteralPath $artifactDir)) {
    Write-Host '  Artifact directory does not exist (likely -WhatIf). Skipping checksum generation.' -ForegroundColor DarkGray
}
else {
    $hashLines = Get-ChildItem -LiteralPath $artifactDir -File -Recurse |
        Where-Object { $_.FullName -ne $checksumPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = Get-RelativePathCompat -BasePath $artifactDir -ChildPath $_.FullName
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "{0} *{1}" -f $hash, $relative.Replace('\\', '/')
        }
    Set-Content -LiteralPath $checksumPath -Value $hashLines -Encoding UTF8
}

if ($GenerateReleaseNotes) {
    Write-Step 'Generating release notes'
    $releaseNotesScriptPath = Join-Path $repoRoot 'deploy\Generate-ReleaseNotes.ps1'
    if (-not (Test-Path -LiteralPath $releaseNotesScriptPath)) {
        throw "Release notes script not found: $releaseNotesScriptPath"
    }

    $releaseNotesTargetPath = $ReleaseNotesOutputPath
    if ([string]::IsNullOrWhiteSpace($releaseNotesTargetPath)) {
        $releaseNotesTargetPath = Join-Path $artifactDir 'RELEASE-NOTES.md'
    }

    if ($PSCmdlet.ShouldProcess($releaseNotesTargetPath, 'Generate release notes markdown')) {
        & $releaseNotesScriptPath -VersionPrefix $VersionPrefix -BuildNumber $BuildNumber -SourceRevisionId $SourceRevisionId -OutputPath $releaseNotesTargetPath
        if ($LASTEXITCODE -ne 0) {
            throw 'Release note generation failed.'
        }
    }
}

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
Write-Host "Version:            $VersionPrefix"
Write-Host "Build:              $BuildNumber"
Write-Host "Source Revision:    $SourceRevisionId"
Write-Host ""
Write-Host 'Deploy on target machine with:' -ForegroundColor Yellow
Write-Host '  1) Copy artifact contents so publish/ and deploy/ are under your AppRoot'
Write-Host '  2) Run deploy/Deploy-BeaconRelay.ps1 -Mode Deploy -NoPublish [other options]'
