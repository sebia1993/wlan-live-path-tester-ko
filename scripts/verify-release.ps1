[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WlanLivePathTester.sln'
$tests = @(
    'tests\WlanLivePathTester.SelfTest\WlanLivePathTester.SelfTest.csproj',
    'tests\WlanLivePathTester.WindowsSmoke\WlanLivePathTester.WindowsSmoke.csproj',
    'tests\WlanLivePathTester.ProxyAuthSmoke\WlanLivePathTester.ProxyAuthSmoke.csproj',
    'tests\WlanLivePathTester.MeasurementSmoke\WlanLivePathTester.MeasurementSmoke.csproj',
    'tests\WlanLivePathTester.ObservationSmoke\WlanLivePathTester.ObservationSmoke.csproj',
    'tests\WlanLivePathTester.ReportSmoke\WlanLivePathTester.ReportSmoke.csproj'
)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Push-Location $root
try {
    Invoke-CheckedCommand `
        -Description 'Validate prerelease workflow contract' `
        -Command {
            powershell -NoProfile -ExecutionPolicy Bypass -File `
                (Join-Path $root `
                    'scripts\test-prerelease-workflow-contract.ps1')
        }

    Invoke-CheckedCommand -Description 'Restore solution' -Command {
        dotnet restore $solution
    }

    Invoke-CheckedCommand -Description 'Build solution' -Command {
        dotnet build $solution -c $Configuration --no-restore
    }

    foreach ($relativeProject in $tests) {
        $project = Join-Path $root $relativeProject
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
            throw "Required smoke-test project not found: $relativeProject"
        }

        Invoke-CheckedCommand -Description "Restore $relativeProject" -Command {
            dotnet restore $project
        }

        Invoke-CheckedCommand -Description "Run $relativeProject" -Command {
            dotnet run --project $project -c $Configuration --no-restore
        }
    }

    Invoke-CheckedCommand -Description 'Audit repository' -Command {
        powershell -NoProfile -ExecutionPolicy Bypass -File `
            (Join-Path $root 'scripts\audit-repository.ps1')
    }

    Invoke-CheckedCommand -Description 'Audit network boundary' -Command {
        powershell -NoProfile -ExecutionPolicy Bypass -File `
            (Join-Path $root 'scripts\audit-network-boundary.ps1')
    }

    Write-Host 'Release verification passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
