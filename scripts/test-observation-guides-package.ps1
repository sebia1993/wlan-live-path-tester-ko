[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PortableZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedZip = [System.IO.Path]::GetFullPath($PortableZip)
if (-not (Test-Path -LiteralPath $resolvedZip -PathType Leaf)) {
    throw "Portable ZIP not found: $resolvedZip"
}

$requiredEntries = @(
    'docs/STRUCTURED_OBSERVATION_TERMINATION.md',
    'docs/UNIFIED_OBSERVATION_TERMINATION_REPORTING.md',
    'docs/OBSERVATION_TERMINATION_FINDINGS.md',
    'docs/INJECTABLE_OBSERVATION_RUNTIME.md',
    'docs/OBSERVATION_TIMING_CONTINUITY.md',
    'docs/OBSERVATION_REPORT_PIPELINE_E2E.md',
    'docs/OBSERVATION_REPORT_TERMINATION_MATRIX.md',
    'docs/BROWSER_OBSERVATION_REPORT.md',
    'docs/OBSERVATION_COUNTER_RESET_FINDING.md',
    'docs/OBSERVATION_POWER_TRANSITIONS.md',
    'docs/OBSERVATION_COMBINED_DISRUPTIONS.md',
    'docs/WLAN_IDENTITY_CONTINUITY.md',
    'docs/RELEASE_NOTES_0.1.0-alpha.10.md'
)

$duplicateRequirements = @($requiredEntries |
    Group-Object |
    Where-Object { $_.Count -gt 1 })
if ($duplicateRequirements.Count -gt 0) {
    $names = @($duplicateRequirements | ForEach-Object { $_.Name })
    throw "Observation guide requirement list contains duplicates: $($names -join ', ')"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedZip)
try {
    $entriesByNormalizedName = @{}
    foreach ($entry in $archive.Entries) {
        $normalizedName = $entry.FullName.Replace('\', '/')
        if ($entriesByNormalizedName.ContainsKey($normalizedName)) {
            throw "Portable ZIP contains duplicate entry: $normalizedName"
        }

        $entriesByNormalizedName[$normalizedName] = $entry
    }

    foreach ($requiredEntry in $requiredEntries) {
        if (-not $entriesByNormalizedName.ContainsKey($requiredEntry)) {
            throw "Portable ZIP is missing observation document: $requiredEntry"
        }

        $entry = $entriesByNormalizedName[$requiredEntry]
        if ($entry.Length -le 0) {
            throw "Observation document is empty in Portable ZIP: $requiredEntry"
        }
    }

    Write-Host "Observation document package validation passed: $($requiredEntries.Count) documents" -ForegroundColor Green
}
finally {
    $archive.Dispose()
}
