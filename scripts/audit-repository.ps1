$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $tracked = @(git ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files 실행에 실패했습니다."
    }

    $forbiddenRoots = @("results/", "reports/", "logs/", "captures/", "temp/", "tmp/")
    $forbiddenExtensions = @(
        ".pcap", ".pcapng", ".cap", ".etl", ".evtx", ".har", ".dmp",
        ".pfx", ".p12", ".pem", ".key", ".cer", ".crt", ".der"
    )

    $violations = New-Object System.Collections.Generic.List[string]

    foreach ($path in $tracked) {
        $normalized = $path.Replace("\", "/")

        if ($forbiddenRoots | Where-Object {
                $normalized.StartsWith($_, [System.StringComparison]::OrdinalIgnoreCase)
            }) {
            $violations.Add("금지된 산출물 경로: $path")
        }

        $extension = [System.IO.Path]::GetExtension($path)
        if ($forbiddenExtensions -contains $extension.ToLowerInvariant()) {
            $violations.Add("금지된 파일 형식: $path")
        }

        if ($normalized -match "^config/(targets|.+\.(local|private))\.json$") {
            $violations.Add("실제 또는 로컬 설정 파일 커밋 금지: $path")
        }
    }

    if ($violations.Count -gt 0) {
        $violations | ForEach-Object { Write-Error $_ }
        exit 1
    }

    Write-Host "Repository audit passed: $($tracked.Count) tracked files checked."
}
finally {
    Pop-Location
}
