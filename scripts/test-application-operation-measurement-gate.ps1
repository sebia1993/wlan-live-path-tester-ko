[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$mainWindowPath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.xaml.cs'
$operationsPath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.ApplicationOperations.cs'
$repeatGatePath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.RepeatedMeasurementOperationGate.cs'
$repeatedPath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.RepeatedMeasurement.cs'

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

function Require-Text {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Text,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Assert-Condition `
        -Condition ($Source.Contains($Text)) `
        -Message "Missing measurement operation contract: $Name"
}

foreach ($path in @(
    $mainWindowPath,
    $operationsPath,
    $repeatGatePath,
    $repeatedPath
)) {
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
        -Message "Required measurement operation file not found: $path"
}

$mainWindow = Get-Content -LiteralPath $mainWindowPath -Raw
$operations = Get-Content -LiteralPath $operationsPath -Raw
$repeatGate = Get-Content -LiteralPath $repeatGatePath -Raw
$repeated = Get-Content -LiteralPath $repeatedPath -Raw

Require-Text `
    -Source $operations `
    -Text '_measurementOperationLease' `
    -Name 'active measurement lease field'
Require-Text `
    -Source $mainWindow `
    -Text 'ApplicationOperationKind.ProxyRouteResolution' `
    -Name 'proxy route resolution operation kind'
Require-Text `
    -Source $mainWindow `
    -Text 'requestCancellation: null' `
    -Name 'non-cancelable proxy route resolution contract'
Require-Text `
    -Source $mainWindow `
    -Text 'ApplicationOperationKind.DownloadMeasurement' `
    -Name 'single download measurement operation kind'
Require-Text `
    -Source $mainWindow `
    -Text 'ApplicationOperationKind operationKind' `
    -Name 'fixed measurement operation kind parameter'
Require-Text `
    -Source $mainWindow `
    -Text 'TryBeginApplicationOperation(' `
    -Name 'central measurement operation acquisition'
Require-Text `
    -Source $mainWindow `
    -Text 'cancellation.Cancel' `
    -Name 'existing measurement cancellation callback registration'
Require-Text `
    -Source $mainWindow `
    -Text '_measurementOperationLease = operationLease;' `
    -Name 'active measurement lease retention'
Require-Text `
    -Source $mainWindow `
    -Text '_measurementOperationLease?.RequestCancellation()' `
    -Name 'measurement cancel through central lease'
Require-Text `
    -Source $mainWindow `
    -Text 'ReferenceEquals(' `
    -Name 'stale measurement finally protection'
Require-Text `
    -Source $mainWindow `
    -Text 'operationLease.Dispose();' `
    -Name 'measurement and proxy route lease release'
Require-Text `
    -Source $repeatGate `
    -Text 'ApplicationOperationKind.RepeatedMeasurement' `
    -Name 'repeated measurement operation classification'
Require-Text `
    -Source $repeatGate `
    -Text 'RunMeasurementOperationAsync(' `
    -Name 'repeated measurement delegation to the common runner'
Require-Text `
    -Source $repeated `
    -Text 'await RunMeasurementOperationAsync(' `
    -Name 'existing repeated measurement runner use'

$measurementMethodIndex = $mainWindow.IndexOf(
    'private async Task RunMeasurementOperationAsync(',
    [StringComparison]::Ordinal)
$measurementTryBeginIndex = $mainWindow.IndexOf(
    'TryBeginApplicationOperation(',
    $measurementMethodIndex,
    [StringComparison]::Ordinal)
$measurementBusyIndex = $mainWindow.IndexOf(
    '_measurementRunning = true;',
    $measurementMethodIndex,
    [StringComparison]::Ordinal)
$measurementUiResetIndex = $mainWindow.IndexOf(
    'SetMeasurementBusy(false);',
    $measurementMethodIndex,
    [StringComparison]::Ordinal)
$measurementLeaseReleaseIndex = $mainWindow.IndexOf(
    'operationLease.Dispose();',
    $measurementMethodIndex,
    [StringComparison]::Ordinal)
Assert-Condition `
    -Condition ($measurementMethodIndex -ge 0 `
        -and $measurementTryBeginIndex -gt $measurementMethodIndex `
        -and $measurementBusyIndex -gt $measurementTryBeginIndex) `
    -Message 'The global measurement lease must be acquired before local busy state is set.'
Assert-Condition `
    -Condition ($measurementUiResetIndex -gt $measurementBusyIndex `
        -and $measurementLeaseReleaseIndex -gt $measurementUiResetIndex) `
    -Message 'Measurement UI state must be restored before the global lease is released.'

$proxyMethodIndex = $mainWindow.IndexOf(
    'private async void OnResolveProxyRouteClick(',
    [StringComparison]::Ordinal)
$proxyTryBeginIndex = $mainWindow.IndexOf(
    'ApplicationOperationKind.ProxyRouteResolution',
    $proxyMethodIndex,
    [StringComparison]::Ordinal)
$proxyDisableIndex = $mainWindow.IndexOf(
    'ResolveProxyRouteButton.IsEnabled = false;',
    $proxyMethodIndex,
    [StringComparison]::Ordinal)
$proxyRestoreIndex = $mainWindow.IndexOf(
    'ResolveProxyRouteButton.IsEnabled = !_measurementRunning;',
    $proxyMethodIndex,
    [StringComparison]::Ordinal)
$proxyLeaseReleaseIndex = $mainWindow.IndexOf(
    'operationLease.Dispose();',
    $proxyMethodIndex,
    [StringComparison]::Ordinal)
Assert-Condition `
    -Condition ($proxyMethodIndex -ge 0 `
        -and $proxyTryBeginIndex -gt $proxyMethodIndex `
        -and $proxyDisableIndex -gt $proxyTryBeginIndex) `
    -Message 'Proxy route resolution must acquire the global lease before changing its UI state.'
Assert-Condition `
    -Condition ($proxyRestoreIndex -gt $proxyDisableIndex `
        -and $proxyLeaseReleaseIndex -gt $proxyRestoreIndex) `
    -Message 'Proxy route UI state must be restored before the global lease is released.'

foreach ($forbidden in @(
    'TryBeginApplicationOperation(url',
    'TryBeginApplicationOperation(target',
    'TryBeginApplicationOperation(targets',
    'TryBeginApplicationOperation(runningMessage',
    'TryBeginApplicationOperation(InternalTargetUrlTextBox',
    'TryBeginApplicationOperation(ExternalTargetUrlsTextBox',
    'operationLease.RequestCancellation();\n        _cancelMeasurement()',
    '측정 처리 중 오류가 발생했습니다: {exception.Message}',
    '프록시 경로 확인 중 오류가 발생했습니다: {exception.Message}'
)) {
    Assert-Condition `
        -Condition (-not $mainWindow.Contains($forbidden)) `
        -Message "Forbidden measurement operation pattern: $forbidden"
}

Write-Host 'Application measurement operation gate contract passed.' `
    -ForegroundColor Green
Write-Host 'Guarded operations: proxy route resolution, single downloads, repeated downloads'
Write-Host 'Proxy route resolution is intentionally non-cancelable and holds its lease until return.'
