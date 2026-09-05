[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$corePath = Join-Path $root `
    'src\WlanLivePathTester.Core\Operations\ApplicationOperationCoordinator.cs'
$helperPath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.ApplicationOperations.cs'
$routePath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.RouteComparisonV3.cs'
$importPath = Join-Path $root `
    'src\WlanLivePathTester.App\MainWindow.RouteProxyImport.cs'

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

foreach ($path in @($corePath, $helperPath, $routePath, $importPath)) {
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
        -Message "Required operation gate file not found: $path"
}

$core = Get-Content -LiteralPath $corePath -Raw
$helper = Get-Content -LiteralPath $helperPath -Raw
$route = Get-Content -LiteralPath $routePath -Raw
$import = Get-Content -LiteralPath $importPath -Raw

Assert-Condition `
    -Condition ($core.Contains('public sealed class ApplicationOperationCoordinator')) `
    -Message 'The Core application operation coordinator is missing.'
Assert-Condition `
    -Condition ($core.Contains('ApplicationOperationLease')) `
    -Message 'The Core operation lease contract is missing.'
Assert-Condition `
    -Condition ($helper.Contains('ApplicationOperationCoordinator')) `
    -Message 'MainWindow does not own the central application operation coordinator.'
Assert-Condition `
    -Condition ($helper.Contains('_routeComparisonOperationLeaseV3')) `
    -Message 'The route-operation lease field is missing.'
Assert-Condition `
    -Condition ($helper.Contains('TryBeginApplicationOperation')) `
    -Message 'The safe MainWindow operation acquisition helper is missing.'
Assert-Condition `
    -Condition ($helper.Contains('ShutdownPending')) `
    -Message 'The acquisition helper must distinguish shutdown rejection.'

$appRoot = Join-Path $root 'src\WlanLivePathTester.App'
$coordinatorOwners = @(Get-ChildItem -LiteralPath $appRoot -File -Filter '*.cs' |
    Where-Object {
        (Get-Content -LiteralPath $_.FullName -Raw).Contains(
            '_applicationOperations = new()')
    })
Assert-Condition `
    -Condition ($coordinatorOwners.Count -eq 1) `
    -Message "Exactly one MainWindow application coordinator owner is required. Actual: $($coordinatorOwners.Name -join ', ')"
Assert-Condition `
    -Condition ($coordinatorOwners[0].Name -ceq `
        'MainWindow.ApplicationOperations.cs') `
    -Message "Unexpected application coordinator owner: $($coordinatorOwners[0].Name)"

Assert-Condition `
    -Condition ($route.Contains('ApplicationOperationKind.RouteComparison')) `
    -Message 'Manual route comparison is not registered as RouteComparison.'
Assert-Condition `
    -Condition ($route.Contains('TryBeginApplicationOperation(')) `
    -Message 'Manual route comparison does not acquire the central operation lease.'
Assert-Condition `
    -Condition ($route.Contains('active.Cancel')) `
    -Message 'Manual route comparison does not register its existing cancellation source.'
Assert-Condition `
    -Condition ($route.Contains('_routeComparisonOperationLeaseV3 = operationLease;')) `
    -Message 'Manual route comparison does not retain the active operation lease.'
Assert-Condition `
    -Condition ($route.Contains('operationLease.RequestCancellation()')) `
    -Message 'Manual route comparison cancel does not flow through the operation lease.'
Assert-Condition `
    -Condition ($route.Contains('operationLease.Dispose();')) `
    -Message 'Manual route comparison does not release its operation lease.'

$routeBusyReset = $route.IndexOf(
    'SetRouteComparisonBusyV3(isBusy: false);',
    [StringComparison]::Ordinal)
$routeLeaseRelease = $route.IndexOf(
    'operationLease.Dispose();',
    [StringComparison]::Ordinal)
Assert-Condition `
    -Condition ($routeBusyReset -ge 0 `
        -and $routeLeaseRelease -gt $routeBusyReset) `
    -Message 'Manual route comparison must restore its UI state before releasing the global lease.'

Assert-Condition `
    -Condition ($import.Contains(
        'ApplicationOperationKind.WindowsProxyImport')) `
    -Message 'Windows proxy import is not registered as WindowsProxyImport.'
Assert-Condition `
    -Condition ($import.Contains(
        'ApplicationOperationKind.RouteComparison')) `
    -Message 'Imported Windows decision comparison is not registered as RouteComparison.'
Assert-Condition `
    -Condition ($import.Contains(
        'ApplicationOperationKind operationKind')) `
    -Message 'The shared route proxy UI runner does not receive a fixed operation kind.'
Assert-Condition `
    -Condition ($import.Contains('TryBeginApplicationOperation(')) `
    -Message 'The route proxy UI runner does not acquire the central lease.'
Assert-Condition `
    -Condition ($import.Contains('_routeComparisonOperationLeaseV3 = operationLease;')) `
    -Message 'The route proxy UI runner does not retain its active lease.'
Assert-Condition `
    -Condition ($import.Contains('operationLease.Dispose();')) `
    -Message 'The route proxy UI runner does not release its lease.'
Assert-Condition `
    -Condition ($import.Contains(
        '_routeComparisonOperationLeaseV3?.RequestCancellation()')) `
    -Message 'Window close does not request route operation cancellation through the lease.'
Assert-Condition `
    -Condition ($import.Contains(
        '&& _routeComparisonOperationLeaseV3 is null')) `
    -Message 'Windows proxy import controls do not include the global lease in their idle state.'

$importBusyReset = $import.IndexOf(
    'SetRouteComparisonBusyV3(isBusy: false);',
    [StringComparison]::Ordinal)
$importLeaseRelease = $import.IndexOf(
    'operationLease.Dispose();',
    [StringComparison]::Ordinal)
Assert-Condition `
    -Condition ($importBusyReset -ge 0 `
        -and $importLeaseRelease -gt $importBusyReset) `
    -Message 'The route proxy UI runner must restore its UI state before releasing the global lease.'

foreach ($source in @($route, $import)) {
    Assert-Condition `
        -Condition (-not $source.Contains(
            'TryBeginApplicationOperation(internalTarget')) `
        -Message 'User-supplied internal targets must never be used as operation identifiers.'
    Assert-Condition `
        -Condition (-not $source.Contains(
            'TryBeginApplicationOperation(proxyDirective')) `
        -Message 'Raw proxy directives must never be used as operation identifiers.'
}

Write-Host 'Application operation route gate contract passed.' `
    -ForegroundColor Green
Write-Host 'Central owner: MainWindow.ApplicationOperations.cs'
Write-Host 'Guarded operations: manual route comparison, Windows proxy import, imported decision comparison'
