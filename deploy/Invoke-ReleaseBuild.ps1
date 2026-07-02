[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$VersionPrefix,

    [string]$BuildNumber = '',
    [string]$SourceRevisionId = '',
    [string]$ReleaseTag = '',
    [string]$TagMessage = '',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$ArtifactRoot = '',
    [string]$ArtifactName = '',
    [switch]$SelfContained,
    [switch]$SkipZip,
    [switch]$AllowDirtyWorkingTree,
    [switch]$SkipTagCreation,
    [switch]$PushTag,
    [switch]$ForceTag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Get-ShortCommit {
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

function Ensure-GitRepository {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git was not found in PATH.'
    }

    $inside = (& git rev-parse --is-inside-work-tree 2>$null)
    if ($LASTEXITCODE -ne 0 -or "$inside".Trim().ToLowerInvariant() -ne 'true') {
        throw 'Current directory is not inside a git repository.'
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScriptPath = Join-Path $repoRoot 'deploy\Build-BeaconRelayArtifact.ps1'
if (-not (Test-Path -LiteralPath $buildScriptPath)) {
    throw "Build script not found: $buildScriptPath"
}

Push-Location $repoRoot
try {
    Ensure-GitRepository

    if ([string]::IsNullOrWhiteSpace($BuildNumber)) {
        $BuildNumber = Get-Date -Format 'yyyyMMddHHmmss'
    }

    if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
        $SourceRevisionId = Get-ShortCommit
    }
    if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
        $SourceRevisionId = 'local'
    }

    if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
        $ReleaseTag = "v$VersionPrefix"
    }
    if ([string]::IsNullOrWhiteSpace($TagMessage)) {
        $TagMessage = "Release $ReleaseTag"
    }

    if (-not $AllowDirtyWorkingTree) {
        $statusLines = (& git status --porcelain)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to inspect working tree state.'
        }

        if ($statusLines -and $statusLines.Count -gt 0) {
            throw 'Working tree has uncommitted changes. Commit/stash changes or use -AllowDirtyWorkingTree.'
        }
    }

    if (-not $SkipTagCreation) {
        Write-Step "Preparing release tag $ReleaseTag"
        $existingTag = (& git tag --list $ReleaseTag)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to query existing git tags.'
        }

        if (-not [string]::IsNullOrWhiteSpace($existingTag)) {
            if (-not $ForceTag) {
                throw "Tag $ReleaseTag already exists. Use -ForceTag to recreate it, or -SkipTagCreation to keep existing tags unchanged."
            }

            if ($PSCmdlet.ShouldProcess($ReleaseTag, 'Delete existing local tag')) {
                & git tag -d $ReleaseTag
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to delete existing local tag $ReleaseTag."
                }
            }
        }

        if ($PSCmdlet.ShouldProcess($ReleaseTag, 'Create annotated release tag')) {
            & git tag -a $ReleaseTag -m $TagMessage
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create tag $ReleaseTag."
            }
        }

        if ($PushTag) {
            if ($PSCmdlet.ShouldProcess($ReleaseTag, 'Push release tag to origin')) {
                & git push origin $ReleaseTag
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to push tag $ReleaseTag to origin."
                }
            }
        }
    }

    Write-Step 'Building release artifact and release notes'
    & $buildScriptPath `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -ArtifactRoot $ArtifactRoot `
        -ArtifactName $ArtifactName `
        -VersionPrefix $VersionPrefix `
        -BuildNumber $BuildNumber `
        -SourceRevisionId $SourceRevisionId `
        -GenerateReleaseNotes `
        -SelfContained:$SelfContained `
        -SkipZip:$SkipZip
    if ($LASTEXITCODE -ne 0) {
        throw 'Release build failed.'
    }

    Write-Step 'Release workflow complete'
    Write-Host "Version:         $VersionPrefix"
    Write-Host "Build Number:    $BuildNumber"
    Write-Host "Source Revision: $SourceRevisionId"
    Write-Host "Release Tag:     $ReleaseTag"
}
finally {
    Pop-Location
}
