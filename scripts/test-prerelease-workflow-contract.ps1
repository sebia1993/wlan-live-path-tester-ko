[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$workflowRoot = Join-Path $root '.github\workflows'
$manualWorkflowPath = Join-Path $workflowRoot 'manual-prerelease.yml'
$scopedTriggerPath = Join-Path $workflowRoot 'scoped-prerelease-trigger.yml'
$legacyTriggerPath = Join-Path $workflowRoot 'prerelease-branch-trigger.yml'
$publishTargetsPath = Join-Path `
    $root `
    'src\WlanLivePathTester.App\Directory.Build.targets'
$packageTestPath = Join-Path `
    $root `
    'scripts\test-observation-guides-package.ps1'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

foreach ($requiredPath in @(
    $manualWorkflowPath,
    $scopedTriggerPath,
    $publishTargetsPath,
    $packageTestPath
)) {
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $requiredPath -PathType Leaf) `
        -Message "Required prerelease contract file not found: $requiredPath"
}

Assert-Condition `
    -Condition (-not (Test-Path -LiteralPath $legacyTriggerPath)) `
    -Message 'The duplicate legacy prerelease branch trigger must remain deleted.'

$manual = Get-Content -LiteralPath $manualWorkflowPath -Raw
$scoped = Get-Content -LiteralPath $scopedTriggerPath -Raw
$publishTargets = Get-Content -LiteralPath $publishTargetsPath -Raw
$packageTest = Get-Content -LiteralPath $packageTestPath -Raw

$defaultTagMatch = [regex]::Match(
    $manual,
    '(?m)^\s*default:\s*["''](?<tag>v[^"'']+)["'']\s*$',
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
Assert-Condition `
    -Condition $defaultTagMatch.Success `
    -Message 'manual-prerelease.yml must expose one quoted default tag.'

$defaultTag = $defaultTagMatch.Groups['tag'].Value
$tagPattern = '^v(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)-(?:[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))$'
$tagMatch = [regex]::Match(
    $defaultTag,
    $tagPattern,
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
Assert-Condition `
    -Condition $tagMatch.Success `
    -Message "The default manual prerelease tag is not strict prerelease SemVer: $defaultTag"

$version = $tagMatch.Groups['version'].Value
$prerelease = $version.Substring($version.IndexOf('-') + 1)
foreach ($identifier in $prerelease.Split('.')) {
    $isNumeric = $identifier -match '^\d+$'
    $hasLeadingZero = $identifier.Length -gt 1 `
        -and $identifier.StartsWith(
            '0',
            [StringComparison]::Ordinal)
    Assert-Condition `
        -Condition (-not ($isNumeric -and $hasLeadingZero)) `
        -Message "The default tag contains a numeric prerelease identifier with a leading zero: $defaultTag"
}

$expectedNotesName = "RELEASE_NOTES_$version.md"
$expectedNotesPath = Join-Path `
    (Join-Path $root 'docs') `
    $expectedNotesName
$nonCanonicalNotesName = "RELEASE_NOTES_$defaultTag.md"
$nonCanonicalNotesPath = Join-Path `
    (Join-Path $root 'docs') `
    $nonCanonicalNotesName

Assert-Condition `
    -Condition (Test-Path -LiteralPath $expectedNotesPath -PathType Leaf) `
    -Message "The default tag does not resolve to an existing release notes file: $expectedNotesName"
Assert-Condition `
    -Condition (-not (Test-Path -LiteralPath $nonCanonicalNotesPath)) `
    -Message "Do not keep a duplicate v-prefixed release notes file: $nonCanonicalNotesName"
Assert-Condition `
    -Condition ((Get-Item -LiteralPath $expectedNotesPath).Length -gt 0) `
    -Message "Release notes file is empty: $expectedNotesName"

$notes = Get-Content -LiteralPath $expectedNotesPath -Raw
Assert-Condition `
    -Condition ($notes.Contains($defaultTag)) `
    -Message "Release notes must identify the exact default tag: $defaultTag"

Assert-Condition `
    -Condition ($manual.Contains('RELEASE_NOTES_$version.md')) `
    -Message 'manual-prerelease.yml must resolve release notes from the validated version without the v prefix.'
Assert-Condition `
    -Condition (-not $manual.Contains('RELEASE_NOTES_$tag.md')) `
    -Message 'manual-prerelease.yml must not resolve release notes from a v-prefixed tag path.'
Assert-Condition `
    -Condition ($publishTargets.Contains($expectedNotesName)) `
    -Message "Application publish targets do not include: $expectedNotesName"
Assert-Condition `
    -Condition ($packageTest.Contains("docs/$expectedNotesName")) `
    -Message "Portable ZIP validation does not require: docs/$expectedNotesName"
Assert-Condition `
    -Condition (-not $publishTargets.Contains($nonCanonicalNotesName)) `
    -Message "Application publish targets still reference the non-canonical notes path: $nonCanonicalNotesName"
Assert-Condition `
    -Condition (-not $packageTest.Contains("docs/$nonCanonicalNotesName")) `
    -Message "Portable ZIP validation still references the non-canonical notes path: docs/$nonCanonicalNotesName"

Assert-Condition `
    -Condition ($scoped.Contains('release-trigger/v*-dispatch')) `
    -Message 'The scoped owner trigger must only monitor release-trigger/v*-dispatch branches.'
Assert-Condition `
    -Condition ($scoped.Contains("`$suffix = '-dispatch'")) `
    -Message 'The scoped trigger must remove the -dispatch suffix before tag validation.'
Assert-Condition `
    -Condition ($scoped.Contains("--ref main")) `
    -Message 'The scoped trigger must dispatch the verified workflow on main.'
Assert-Condition `
    -Condition ($scoped.Contains('manual-prerelease.yml')) `
    -Message 'The scoped trigger must dispatch only manual-prerelease.yml.'

$releaseBranchWatchers = @()
foreach ($workflowFile in Get-ChildItem `
    -LiteralPath $workflowRoot `
    -File `
    -Filter '*.yml') {
    $content = Get-Content -LiteralPath $workflowFile.FullName -Raw
    if ($content.Contains('release-trigger/v*')) {
        $releaseBranchWatchers += $workflowFile.Name
    }
}
$releaseBranchWatchers = @($releaseBranchWatchers |
    Sort-Object -Unique)
Assert-Condition `
    -Condition ($releaseBranchWatchers.Count -eq 1) `
    -Message "Exactly one workflow may watch release-trigger/v* branches. Actual: $($releaseBranchWatchers -join ', ')"
Assert-Condition `
    -Condition ($releaseBranchWatchers[0] -ceq `
        'scoped-prerelease-trigger.yml') `
    -Message "Unexpected release trigger workflow: $($releaseBranchWatchers[0])"

Write-Host 'Prerelease workflow contract validation passed.' `
    -ForegroundColor Green
Write-Host "Default tag: $defaultTag"
Write-Host "Canonical release notes: docs/$expectedNotesName"
Write-Host 'Release branch watcher: scoped-prerelease-trigger.yml'
