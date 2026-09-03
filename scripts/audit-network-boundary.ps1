$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root "src"
$configExample = Join-Path $root "config/targets.example.json"

$forbiddenPatterns = @(
    "api\.ipify\.",
    "ipinfo\.io",
    "speedtest\.net",
    "fast\.com",
    "openai",
    "anthropic",
    "HttpMethod\.Post",
    "HttpMethod\.Put",
    "HttpMethod\.Patch",
    "HttpMethod\.Delete",
    "UploadData",
    "UploadFile",
    "TelemetryClient",
    "SentrySdk"
)

$files = @(
    Get-ChildItem -Path $sourceRoot -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".xaml", ".csproj", ".json") }
)

$violations = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match $pattern) {
            $relative = $file.FullName.Substring($root.Length).TrimStart([char[]]"\/")
            $violations.Add("금지된 통신 또는 외부 서비스 패턴 '$pattern': $relative")
        }
    }
}

$runtimePackages = @(
    Get-ChildItem -Path $sourceRoot -Recurse -Filter *.csproj -File |
        Select-String -Pattern "<PackageReference"
)

if ($runtimePackages.Count -gt 0) {
    foreach ($match in $runtimePackages) {
        $relative = $match.Path.Substring($root.Length).TrimStart([char[]]"\/")
        $violations.Add("런타임 PackageReference 금지: $relative")
    }
}

if (Test-Path -LiteralPath $configExample) {
    $example = Get-Content -LiteralPath $configExample -Raw
    $allowedExampleHosts = @("example.invalid", "192.0.2.10")
    $urls = [regex]::Matches($example, "https?://([^/`"\s]+)")

    foreach ($urlMatch in $urls) {
        $host = $urlMatch.Groups[1].Value.Split(":")[0]
        if ($allowedExampleHosts -notcontains $host) {
            $violations.Add("예제 설정에 허용되지 않은 실제 호스트가 있습니다: $host")
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "Network boundary audit passed: $($files.Count) source files checked."
