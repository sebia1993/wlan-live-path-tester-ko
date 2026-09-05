[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ui = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.App\MainWindow.RouteComparisonReportV2.cs') -Raw -Encoding UTF8
$session = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.Core\Reporting\ReportSaveSession.cs') -Raw -Encoding UTF8

foreach ($required in @(
    'ReportSaveSession _routeReportSaveSession',
    '_routeReportSaveSession.TryStart(',
    'document, directory, "WlanRouteComparison", token',
    'OnCancelRouteComparisonReportV2',
    '_routeReportSaveSession.RequestCancellation()',
    'Closing += OnRouteReportWindowClosing',
    'Closing -= OnRouteReportWindowClosing',
    'e.Cancel = true',
    'await _routeReportSaveSession.CancelAndWaitAsync()',
    'await _routeReportUiSettled',
    'Dispatcher.InvokeAsync(',
    '_routeReportNeedsReview',
    'CollectionChanged += OnRouteReportTabsChanged',
    'CollectionChanged -= OnRouteReportTabsChanged',
    'catch (ReportFileSetRecoveryException)',
    'catch (OperationCanceledException)',
    'SetRouteReportSaveBusy(false)'
)) {
    if (-not $ui.Contains($required)) { throw "Missing route report UI contract: $required" }
}
foreach ($forbidden in @(
    '.Wait(', '.Result', '.GetAwaiter()', 'exception.Message',
    'HttpClient', 'WebRequest', 'WinHttp', 'Dns.Get',
    'run.Message', 'run.InternalRouteEvidence', 'run.ProxyExecution'
)) {
    if ($ui.Contains($forbidden)) { throw "Forbidden route report UI pattern: $forbidden" }
}
foreach ($required in @(
    'TaskCreationOptions.RunContinuationsAsynchronously',
    'await source.CancelAsync()',
    'await cancellation.ConfigureAwait(false)',
    'source.Dispose()',
    'finished.TrySetResult(result!)',
    'ReportSaveSessionState.Closing',
    '_closed || _active is not null'
)) {
    if (-not $session.Contains($required)) { throw "Missing save session contract: $required" }
}
Write-Host 'Route report save UI source contract passed.' -ForegroundColor Green
Write-Host 'This is a source-level check, not a WPF interaction test.'
