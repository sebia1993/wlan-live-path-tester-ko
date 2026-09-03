$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root "src"
$configExample = Join-Path $root "config/targets.example.json"

$forbiddenPatterns = @(
    'api\.ipify\.',
    'ipinfo\.io',
    'speedtest\.net',
    'fast\.com',
    'openai',
    'anthropic',
    'HttpMethod\.Post',
    'HttpMethod\.Put',
    'HttpMethod\.Patch',
    'HttpMethod\.Delete',
    'UploadData',
    'UploadFile',
    'TelemetryClient',
    'SentrySdk'
)

$sourceExtensions = @('.cs', '.xaml', '.csproj', '.json')
$files = @(
    Get-ChildItem -Path $sourceRoot -Recurse -File |
        Where-Object { $sourceExtensions.Contains($_.Extension.ToLowerInvariant()) }
)

$violations = New-Object "System.Collections.Generic.List[string]"

foreach ($file in $files) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match $pattern) {
            $relative = $file.FullName.Substring($root.Length).TrimStart([char[]]"\/")
            [void]$violations.Add("Forbidden network or external-service pattern '$pattern': $relative")
        }
    }
}

$runtimePackages = @(
    Get-ChildItem -Path $sourceRoot -Recurse -Filter *.csproj -File |
        Select-String -Pattern '<PackageReference'
)

foreach ($match in $runtimePackages) {
    $relative = $match.Path.Substring($root.Length).TrimStart([char[]]"\/")
    [void]$violations.Add("Runtime PackageReference is not allowed without an ADR: $relative")
}

if (Test-Path -LiteralPath $configExample) {
    $example = Get-Content -LiteralPath $configExample -Raw
    $allowedExampleHosts = @('example.invalid', '192.0.2.10')
    $urls = [regex]::Matches($example, 'https?://([^/"\s]+)')

    foreach ($urlMatch in $urls) {
        $targetHost = $urlMatch.Groups[1].Value.Split(':')[0]
        if (-not $allowedExampleHosts.Contains($targetHost)) {
            [void]$violations.Add("Example configuration contains a non-documentation host: $targetHost")
        }
    }
}

if ($violations.Count -gt 0) {
    foreach ($violation in $violations) {
        Write-Error $violation
    }
    exit 1
}

Write-Host "Network boundary audit passed: $($files.Count) source files checked."
