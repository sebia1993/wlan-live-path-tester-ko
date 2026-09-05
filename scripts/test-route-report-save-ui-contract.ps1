[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ui = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.App\MainWindow.RouteComparisonReportV2.cs') -Raw -Encoding UTF8
$operations = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.App\MainWindow.ApplicationOperations.cs') -Raw -Encoding UTF8
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
    'SetRouteReportSaveBusy(false)',
    'ApplicationOperationKind.RouteComparisonReportSave',
    'TryBeginApplicationOperation(',
    'Interlocked.Exchange(ref operationCancellationRequested, 1)',
    'Volatile.Read(ref operationCancellationRequested)',
    'RequestRouteReportCancellationV2()',
    '_routeReportOperationLeaseV2?.RequestCancellation()',
    'ReferenceEquals(_routeReportOperationLeaseV2, operationLease)',
    'operationLease.Dispose()'
)) {
    if (-not $ui.Contains($required)) { throw "Missing route report UI contract: $required" }
}
foreach ($required in @(
    'ApplicationOperationLease?',
    '_routeReportOperationLeaseV2'
)) {
    if (-not $operations.Contains($required)) { throw "Missing route report operation field contract: $required" }
}
foreach ($forbidden in @(
    '.Wait(', '.Result', '.GetAwaiter()', 'exception.Message',
    'HttpClient', 'WebRequest', 'WinHttp', 'Dns.Get',
    'run.Message', 'run.InternalRouteEvidence', 'run.ProxyExecution',
    'TryBeginApplicationOperation(run',
    'TryBeginApplicationOperation(directory'
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

$tryStartIndex = $ui.IndexOf(
    '_routeReportSaveSession.TryStart(',
    [StringComparison]::Ordinal)
$postStartLatchIndex = $ui.IndexOf(
    'Volatile.Read(ref operationCancellationRequested)',
    $tryStartIndex + 1,
    [StringComparison]::Ordinal)
$sessionCancelIndex = $ui.IndexOf(
    '_routeReportSaveSession.RequestCancellation();',
    $postStartLatchIndex,
    [StringComparison]::Ordinal)
if ($tryStartIndex -lt 0 -or $postStartLatchIndex -le $tryStartIndex `
    -or $sessionCancelIndex -le $postStartLatchIndex) {
    throw 'Route report save must re-apply a latched global cancellation after ReportSaveSession.TryStart.'
}

$busyResetIndex = $ui.LastIndexOf(
    'SetRouteReportSaveBusy(false)',
    [StringComparison]::Ordinal)
$uiSettledIndex = $ui.LastIndexOf(
    'uiSettled.TrySetResult(true)',
    [StringComparison]::Ordinal)
$leaseReleaseIndex = $ui.LastIndexOf(
    'operationLease.Dispose()',
    [StringComparison]::Ordinal)
if ($busyResetIndex -lt 0 -or $uiSettledIndex -le $busyResetIndex `
    -or $leaseReleaseIndex -le $uiSettledIndex) {
    throw 'Route report UI and settled task must complete before the global operation lease is released.'
}

Write-Host 'Route report save UI source contract passed.' -ForegroundColor Green
Write-Host 'Global RouteComparisonReportSave lease wraps the existing ReportSaveSession.' -ForegroundColor Green
Write-Host 'This is a source-level check, not a WPF interaction test.'
