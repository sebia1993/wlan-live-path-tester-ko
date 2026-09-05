[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'test-observation-guides-package.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw 'Diagnostic guide validator is missing.'
}
# Independent fixture list: do not derive expected requirements from the validator.
$required = @(
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
    'docs/RELEASE_NOTES_0.1.0-alpha.10.md',
    'docs/INTERNAL_PROXY_ROUTE_COMPARISON_COORDINATOR_V2.md',
    'docs/INTERNAL_PROXY_ROUTE_COMPARISON_RUN_FINDINGS_V2.md',
    'docs/INTERNAL_PROXY_ROUTE_COMPARISON_UI_V3.md',
    'docs/ROUTE_COMPARISON_REPORT_EXPORT.md'
)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('WlanGuideCases-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temp) | Out-Null

function Assert-PackageCase {
    param([string]$Mode, [string]$ExpectedFailure)
    $zipPath = Join-Path $temp ($Mode + '.zip')
    $spec = @($required | ForEach-Object { [pscustomobject]@{ Name = $_; Content = 'synthetic guide' } })
    switch ($Mode) {
        'missing' { $spec = @($spec | Where-Object { $_.Name -ne 'docs/ROUTE_COMPARISON_REPORT_EXPORT.md' }) }
        'empty' { $spec[-1].Content = '' }
        'wrong-case' { $spec[-1].Name = $spec[-1].Name.ToLowerInvariant() }
        'duplicate' { $spec += [pscustomobject]@{ Name = $required[-1]; Content = 'duplicate' } }
        'case-collision' { $spec += [pscustomobject]@{ Name = $required[-1].ToLowerInvariant(); Content = 'collision' } }
        'separator-collision' { $spec += [pscustomobject]@{ Name = $required[-1].Replace('/', '\'); Content = 'collision' } }
        'backslash' { foreach ($item in $spec) { $item.Name = $item.Name.Replace('/', '\') } }
    }
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $spec) {
            $entry = $archive.CreateEntry($item.Name)
            $stream = $entry.Open()
            try {
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($item.Content)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $failure = $null
    try { & $validator -PortableZip $zipPath | Out-Null }
    catch { $failure = $_.Exception.Message }
    if ([string]::IsNullOrEmpty($ExpectedFailure)) {
        if ($null -ne $failure) { throw "Valid fixture rejected ($Mode): $failure" }
    }
    elseif ($null -eq $failure -or -not $failure.Contains($ExpectedFailure)) {
        throw "Fixture $Mode did not fail with the expected reason: $ExpectedFailure. Actual: $failure"
    }
    Write-Host "PASS diagnostic guide package case: $Mode"
}
try {
    Assert-PackageCase -Mode 'valid' -ExpectedFailure ''
    Assert-PackageCase -Mode 'backslash' -ExpectedFailure ''
    Assert-PackageCase -Mode 'missing' -ExpectedFailure 'missing observation document'
    Assert-PackageCase -Mode 'empty' -ExpectedFailure 'document is empty'
    Assert-PackageCase -Mode 'wrong-case' -ExpectedFailure 'incorrect case'
    Assert-PackageCase -Mode 'duplicate' -ExpectedFailure 'duplicate entry'
    Assert-PackageCase -Mode 'case-collision' -ExpectedFailure 'duplicate entry'
    Assert-PackageCase -Mode 'separator-collision' -ExpectedFailure 'duplicate entry'
    Write-Host 'All 8 diagnostic guide package cases passed.'
}
finally {
    # This directory is a randomly named test fixture, never a user report directory.
    if ([System.IO.Directory]::Exists($temp)) { [System.IO.Directory]::Delete($temp, $true) }
}
