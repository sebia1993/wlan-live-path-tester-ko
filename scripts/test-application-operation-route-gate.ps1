[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\WlanLivePathTester.App'
function Read-App([string]$Name) { Get-Content -LiteralPath (Join-Path $app $Name) -Raw -Encoding UTF8 }
function Require([string]$Source, [string]$Text) {
    if (-not $Source.Contains($Text)) { throw "Missing central operation contract: $Text" }
}
$central = Read-App 'MainWindow.ApplicationOperations.cs'
$route = Read-App 'MainWindow.RouteOperation.cs'
$diagnostic = Read-App 'MainWindow.LocalDiagnosticOperation.cs'
$comparison = Read-App 'MainWindow.RouteComparisonV3.cs'
$import = Read-App 'MainWindow.RouteProxyImport.cs'
$report = Read-App 'MainWindow.RouteComparisonReportV2.cs'
$adapter = Read-App 'ApplicationOperationUiSession.cs'
$files = @(Get-ChildItem -LiteralPath $app -Filter '*.cs' -File)
$joined = ($files | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$owners = [regex]::Matches($joined, 'ApplicationOperationCoordinator\s+_applicationOperations\s*=\s*new\(\)').Count
if ($owners -ne 1) { throw "Expected exactly one MainWindow coordinator owner, found $owners" }
$closingHooks = [regex]::Matches($joined, '\bClosing\s*\+=').Count
if ($closingHooks -ne 1) { throw "Expected one application Closing subscription, found $closingHooks" }
foreach ($required in @('Closing += OnApplicationOperationClosing', 'Closing -= OnApplicationOperationClosing',
    'session.Snapshot.IsBusy', 'await session.RequestShutdownAsync()', 'Dispatcher.InvokeAsync(',
    'session.CancelShutdownRequest()', 'KeepWindowOpenForRouteReportReview()',
    'KeepWindowOpenForLocalReportReview()', 'KeepWindowOpenForAuxiliaryReportReview()')) { Require $central $required }
foreach ($required in @('RunLocalDiagnosticAsync(', 'ApplicationOperationKind.RouteComparison',
    'ApplicationOperationKind.WindowsProxyImport', 'CancelLocalDiagnostic(kind)',
    'SetRouteComparisonBusyV3(isBusy: busy)', 'UpdateRouteProxyImportControls()')) { Require $route $required }
foreach ($required in @('using ApplicationOperationUiLease?', 'TryBeginUiApplicationOperation(',
    'userCancellation.Cancel', 'combined.Token.ThrowIfCancellationRequested()', 'setBusy(false)')) { Require $diagnostic $required }
foreach ($required in @('ApplicationOperationKind.RouteComparison', 'RunRouteUiOperationAsync(',
    'ApplyRouteComparisonResult', '.RunManualDirectiveAsync(')) { Require $comparison $required }
foreach ($required in @('ApplicationOperationKind.WindowsProxyImport', 'ApplicationOperationKind.RouteComparison',
    'RunRouteUiOperationAsync', '_windowsRouteProxyImporter.ImportAsync(', '_routeComparisonCoordinatorV3.RunAsync(')) { Require $import $required }
foreach ($required in @('ApplicationOperationKind.RouteComparisonReportSave', 'using ApplicationOperationUiLease?',
    'TryBeginUiApplicationOperation(', '_routeReportSaveSession.TryStart(', 'lease.RequestCancellation()')) { Require $report $required }
foreach ($required in @('lease.RestorePeerTabs()', 'lease.CoreLease.Dispose()', 'BindingOperations.SetBinding(')) { Require $adapter $required }
if ($adapter.IndexOf('lease.RestorePeerTabs()') -gt $adapter.IndexOf('lease.CoreLease.Dispose()')) {
    throw 'UI restoration must precede Core release.'
}
foreach ($retired in @('TryBeginApplicationOperation(', '_routeComparisonPeerTabStatesV3',
    '_routeProxyPeerCollection', '_routeReportPeerStates', '_routeProxyOperationCompletion',
    'OnRouteProxyImportClosing', 'OnRouteReportWindowClosing', 'FinishDeferredRouteReportCloseAsync')) {
    if ($joined.Contains($retired)) { throw "Retired parallel route lifetime reintroduced: $retired" }
}
Write-Host 'Central route/UI operation contract passed: one coordinator and one Closing owner.' -ForegroundColor Green
Write-Host 'Runtime exclusion, binding restoration, cancellation and close are additionally exercised by WPF smoke tests.'
