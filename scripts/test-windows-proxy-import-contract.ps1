[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ui = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.App\MainWindow.RouteProxyImport.cs') -Raw -Encoding UTF8
$reader = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.Windows\Proxy\WindowsRouteProxyImporter.cs') -Raw -Encoding UTF8
$tests = Get-Content -LiteralPath (Join-Path $root 'tests\WlanLivePathTester.WindowsSmoke\WindowsRouteProxyImporterTests.cs') -Raw -Encoding UTF8
$main = Get-Content -LiteralPath (Join-Path $root 'tests\WlanLivePathTester.WindowsSmoke\Program.cs') -Raw -Encoding UTF8
$bootstrap = Get-Content -LiteralPath (Join-Path $root 'src\WlanLivePathTester.App\RouteComparisonV3Bootstrap.cs') -Raw -Encoding UTF8
function Require-Text([string]$Source, [string]$Text) {
    if (-not $Source.Contains($Text)) { throw "Missing import contract: $Text" }
}
function Forbid-Text([string]$Source, [string]$Text) {
    if ($Source.Contains($Text)) { throw "Forbidden import contract: $Text" }
}
foreach ($text in @(
    'IsChecked = false', 'allowAutomatic = _allowAutomaticRouteProxy?.IsChecked == true',
    '_windowsRouteProxyImporter.ImportAsync(', 'imported.TryGetSelection(target, out',
    '_routeComparisonCoordinatorV3.RunAsync(', '_latestRouteComparisonRunV3 = run',
    'token.ThrowIfCancellationRequested()', '_importedRouteProxy = null',
    'e.Cancel = true', 'await pending', 'Dispatcher.BeginInvoke(',
    'TextChanged -= OnRouteProxyTargetChanged', 'Closing -= OnRouteProxyImportClosing',
    'Closed -= OnRouteProxyImportClosed', 'IsEnabledChanged -= OnRouteProxyBusyChanged'
)) { Require-Text $ui $text }
foreach ($text in @(
    'RunManualDirectiveAsync(', '_routeComparisonProxyDirectiveV3.Text =',
    'HttpClient', 'WinHttpRequestExecutor', 'Dns.Get', 'Marshal.',
    'exception.Message', '.Wait()', '.Result'
)) { Forbid-Text $ui $text }
foreach ($text in @(
    'CurrentUserProxySettingsReader.ReadRaw', 'ProxyRouteResolver.ResolveDetailed(',
    'if (!allowAutomatic)', 'ProxyConfigurationSource.Pac or ProxyConfigurationSource.Wpad',
    'ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot)',
    'Interlocked.CompareExchange(ref _running, 1, 0)', 'Volatile.Write(ref _running, 0)',
    'CancellationToken.None', '_clock.GetElapsedTime(_timestamp)',
    'TimeSpan.FromMinutes(5)', 'StringComparison.Ordinal'
)) { Require-Text $reader $text }
foreach ($text in @('DllImport', 'LibraryImport', 'WinHttpOpen(', 'HttpClient', 'Task.WhenAny(', '.WaitAsync(')) {
    Forbid-Text $reader $text
}
foreach ($text in @(
    'AutomaticLookupRequiresConsentEvenWithManualSettings',
    'FailedOrFallbackAutomaticDecisionIsRejected',
    'PartialAndDirectFirstAutomaticResultsAreRejected',
    'CancellationWaitsForNativeReturnAndBlocksReentry',
    'TargetAndMonotonicAgeAreRequired', 'PublicResultDoesNotExposeRawData'
)) { Require-Text $tests $text }
Forbid-Text $tests '[ModuleInitializer]'
Require-Text $main 'WindowsRouteProxyImporterTests.RunAsync().GetAwaiter().GetResult()'
Require-Text $bootstrap 'window.EnsureRouteProxyImportControls()'
Write-Host 'Windows proxy import source contract passed (not a WPF interaction test).' -ForegroundColor Green
