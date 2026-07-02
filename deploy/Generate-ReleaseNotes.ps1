[CmdletBinding()]
param(
    [string]$VersionPrefix = '',
    [string]$BuildNumber = '',
    [string]$SourceRevisionId = '',
    [string]$TagPattern = 'v[0-9]*',
    [string]$FromRef = '',
    [string]$ToRef = 'HEAD',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
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

function Resolve-PreviousTag {
    param(
        [string]$TargetRef,
        [string]$Pattern
    )

    try {
        $tag = (& git describe --tags --abbrev=0 --match $Pattern "$TargetRef^" 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($tag)) {
            return $tag.Trim()
        }
    }
    catch {
    }

    return ''
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git was not found in PATH. Release notes require git history.'
    }

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
        $SourceRevisionId = Get-ShortCommit
    }
    if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
        $SourceRevisionId = 'local'
    }

    if ([string]::IsNullOrWhiteSpace($FromRef)) {
        $FromRef = Resolve-PreviousTag -TargetRef $ToRef -Pattern $TagPattern
    }

    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        $versionLabel = "v$VersionPrefix-build.$BuildNumber"
        $releaseNotesDir = Join-Path $repoRoot 'artifacts\release-notes'
        if (-not (Test-Path -LiteralPath $releaseNotesDir)) {
            New-Item -Path $releaseNotesDir -ItemType Directory -Force | Out-Null
        }

        $OutputPath = Join-Path $releaseNotesDir ("RELEASE-NOTES-$versionLabel.md")
    }
    else {
        $outputDir = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path -LiteralPath $outputDir)) {
            New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
        }
    }

    Write-Step 'Collecting commit history for release notes'

    $logRangeLabel = if ([string]::IsNullOrWhiteSpace($FromRef)) { "(initial commit)..$ToRef" } else { "$FromRef..$ToRef" }
    $commitRange = if ([string]::IsNullOrWhiteSpace($FromRef)) { $ToRef } else { "$FromRef..$ToRef" }

    $commitLines = (& git log $commitRange --date=short --pretty=format:"%h|%ad|%an|%s")
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to query git log for release notes.'
    }

    $commits = @()
    foreach ($line in $commitLines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = $line -split '\|', 4
        if ($parts.Length -lt 4) {
            continue
        }

        $commits += [pscustomobject]@{
            Sha = $parts[0]
            Date = $parts[1]
            Author = $parts[2]
            Subject = $parts[3]
        }
    }

    $generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
    $versionLine = "$VersionPrefix-build.$BuildNumber"
    $content = New-Object System.Collections.Generic.List[string]
    $null = $content.Add("# Release Notes - $versionLine")
    $null = $content.Add('')
    $null = $content.Add("- Generated (UTC): $generatedUtc")
    $null = $content.Add("- Source Revision: $SourceRevisionId")
    $null = $content.Add("- Tag Pattern: $TagPattern")
    $null = $content.Add("- Commit Range: $logRangeLabel")
    $null = $content.Add('')

    if ($commits.Count -eq 0) {
        $null = $content.Add('## Changes')
        $null = $content.Add('')
        $null = $content.Add('- No commits found in the selected range.')
    }
    else {
        $null = $content.Add('## Changes')
        $null = $content.Add('')
        foreach ($commit in $commits) {
            $null = $content.Add("- $($commit.Subject) ($($commit.Sha), $($commit.Date), $($commit.Author))")
        }

        $null = $content.Add('')
        $null = $content.Add('## Included Commits')
        $null = $content.Add('')
        foreach ($commit in $commits) {
            $null = $content.Add("- $($commit.Sha) - $($commit.Subject)")
        }
    }

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8

    Write-Step 'Release notes complete'
    Write-Host "Output Path: $OutputPath"
    Write-Host "Version:     $VersionPrefix"
    Write-Host "Build:       $BuildNumber"
    Write-Host "Revision:    $SourceRevisionId"
    Write-Host "Range:       $logRangeLabel"
}
finally {
    Pop-Location
}
