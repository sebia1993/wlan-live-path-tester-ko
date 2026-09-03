$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $tracked = @(git ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed."
    }

    $forbiddenRoots = @("results/", "reports/", "logs/", "captures/", "temp/", "tmp/")
    $forbiddenExtensions = @(
        ".pcap", ".pcapng", ".cap", ".etl", ".evtx", ".har", ".dmp",
        ".pfx", ".p12", ".pem", ".key", ".cer", ".crt", ".der"
    )

    $violations = New-Object "System.Collections.Generic.List[string]"

    foreach ($path in $tracked) {
        $normalized = $path.Replace([char]92, [char]47)

        foreach ($prefix in $forbiddenRoots) {
            if ($normalized.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$violations.Add("Forbidden generated-output path: $path")
                break
            }
        }

        $extension = [System.IO.Path]::GetExtension($path)
        if ($extension -and $forbiddenExtensions.Contains($extension.ToLowerInvariant())) {
            [void]$violations.Add("Forbidden file type: $path")
        }

        if ($normalized -match '^config/(targets|.+\.(local|private))\.json$') {
            [void]$violations.Add("Local or real target configuration must not be committed: $path")
        }
    }

    if ($violations.Count -gt 0) {
        foreach ($violation in $violations) {
            Write-Error $violation
        }
        exit 1
    }

    Write-Host "Repository audit passed: $($tracked.Count) tracked files checked."
}
finally {
    Pop-Location
}
