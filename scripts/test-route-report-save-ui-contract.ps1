[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Read-Source([string]$Path) { Get-Content -LiteralPath (Join-Path $root $Path) -Raw -Encoding UTF8 }
$ui = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteComparisonReportV2.cs'
$central = Read-Source 'src\WlanLivePathTester.App\MainWindow.ApplicationOperations.cs'
$session = Read-Source 'src\WlanLivePathTester.Core\Reporting\ReportSaveSession.cs'
function Require([string]$Source, [string]$Text) {
    if (-not $Source.Contains($Text)) { throw "Missing report lifetime contract: $Text" }
}
foreach ($text in @('ReportSaveSession _routeReportSaveSession', '_routeReportSaveSession.TryStart(',
    '"WlanRouteComparison", token', 'ApplicationOperationKind.RouteComparisonReportSave',
    'using ApplicationOperationUiLease?', 'TryBeginUiApplicationOperation(',
    'Interlocked.Exchange(ref cancellationRequested, 1)', 'Volatile.Read(ref cancellationRequested)',
    '_routeReportSaveSession.RequestCancellation()', 'RouteReportSaveOutput saved = await completion',
    'lease.RequestCancellation()', 'catch (ReportFileSetRecoveryException)', 'catch (OperationCanceledException)',
    'CancellationCallbackFailed', 'SetRouteReportSaveBusy(false)',
    'ReferenceEquals(_routeReportUiLease, lease)', 'KeepWindowOpenForRouteReportReview()')) { Require $ui $text }
$start = $ui.IndexOf('_routeReportSaveSession.TryStart(', [StringComparison]::Ordinal)
$latch = $ui.IndexOf('Volatile.Read(ref cancellationRequested)', $start, [StringComparison]::Ordinal)
$reapply = $ui.IndexOf('_routeReportSaveSession.RequestCancellation()', $latch, [StringComparison]::Ordinal)
if ($start -lt 0 -or $latch -le $start -or $reapply -le $latch) { throw 'Post-start cancellation latch must be reapplied.' }
foreach ($text in @('await session.RequestShutdownAsync()', 'KeepWindowOpenForRouteReportReview()',
    'Dispatcher.InvokeAsync(', 'session.CancelShutdownRequest()')) { Require $central $text }
foreach ($text in @('TaskCreationOptions.RunContinuationsAsynchronously', 'await source.CancelAsync()',
    'await cancellation.ConfigureAwait(false)', 'source.Dispose()', 'finished.TrySetResult(result!)',
    'ReportSaveSessionState.Closing', '_closed || _active is not null')) { Require $session $text }
foreach ($text in @('.Wait(', '.Result', '.GetAwaiter()', 'exception.Message', 'HttpClient', 'WebRequest',
    'WinHttp', 'Dns.Get', 'run.Message', 'run.InternalRouteEvidence', 'run.ProxyExecution',
    'OnRouteReportWindowClosing', '_routeReportPeerStates', 'Close();')) {
    if ($ui.Contains($text)) { throw "Forbidden route report UI pattern: $text" }
}
Write-Host 'Route report shared UI lease, cancellation latch, commit semantics and recovery contract passed.' -ForegroundColor Green
Write-Host 'ReportSaveSession joins writer and cancellation callbacks before central close can complete.'
