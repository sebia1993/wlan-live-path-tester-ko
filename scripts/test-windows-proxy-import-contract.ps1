[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Read-Source([string]$Path) { Get-Content -LiteralPath (Join-Path $root $Path) -Raw -Encoding UTF8 }
$ui = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteProxyImport.cs'
$central = Read-Source 'src\WlanLivePathTester.App\MainWindow.ApplicationOperations.cs'
$lifetime = Read-Source 'src\WlanLivePathTester.App\MainWindow.LocalDiagnosticOperation.cs'
$reader = Read-Source 'src\WlanLivePathTester.Windows\Proxy\WindowsRouteProxyImporter.cs'
$tests = Read-Source 'tests\WlanLivePathTester.WindowsSmoke\WindowsRouteProxyImporterTests.cs'
$main = Read-Source 'tests\WlanLivePathTester.WindowsSmoke\Program.cs'
$bootstrap = Read-Source 'src\WlanLivePathTester.App\RouteComparisonV3Bootstrap.cs'
function Require([string]$Source, [string]$Text) {
    if (-not $Source.Contains($Text)) { throw "Missing import contract: $Text" }
}
function Forbid([string]$Source, [string]$Text) {
    if ($Source.Contains($Text)) { throw "Forbidden import contract: $Text" }
}
foreach ($text in @('IsChecked = false', '_windowsRouteProxyImporter.ImportAsync(',
    'imported.TryGetSelection(target, out', '_routeComparisonCoordinatorV3.RunAsync(',
    'ApplyRouteComparisonResult(run)', 'token.ThrowIfCancellationRequested()', '_importedRouteProxy = null',
    'RunRouteUiOperationAsync', 'revision != _routeProxyTargetRevision',
    'TextChanged -= OnRouteProxyTargetChanged', 'Closed -= OnRouteProxyImportClosed')) { Require $ui $text }
if (-not [regex]::IsMatch($ui, 'allowAutomatic\s*=\s*_allowAutomaticRouteProxy\?\.IsChecked\s*==\s*true')) {
    throw 'Explicit PAC/WPAD consent checkbox assignment is missing.'
}
foreach ($text in @('e.Cancel = true', 'await session.RequestShutdownAsync()', 'Dispatcher.InvokeAsync(',
    'Closing -= OnApplicationOperationClosing')) { Require $central $text }
foreach ($text in @('ApplicationOperationKind.WindowsProxyImport', 'combined.Token.ThrowIfCancellationRequested()',
    'userCancellation.Cancel', 'TryBeginUiApplicationOperation(')) { Require $lifetime $text }
foreach ($text in @('RunManualDirectiveAsync(', '_routeComparisonProxyDirectiveV3.Text =',
    'HttpClient', 'WinHttpRequestExecutor', 'Dns.Get', 'Marshal.', 'exception.Message', '.Wait()', '.Result',
    'OnRouteProxyImportClosing', '_routeProxyPeerCollection')) { Forbid $ui $text }
foreach ($text in @('CurrentUserProxySettingsReader.ReadRaw', 'ProxyRouteResolver.ResolveDetailed(',
    'if (!allowAutomatic)', 'ProxyConfigurationSource.Pac or ProxyConfigurationSource.Wpad',
    'ProxyDirectiveSourceSnapshotSelectionPolicy.Select(snapshot)',
    'Interlocked.CompareExchange(ref _running, 1, 0)', 'Volatile.Write(ref _running, 0)',
    'CancellationToken.None', '_clock.GetElapsedTime(_timestamp)', 'TimeSpan.FromMinutes(5)', 'StringComparison.Ordinal')) { Require $reader $text }
foreach ($text in @('DllImport', 'LibraryImport', 'WinHttpOpen(', 'HttpClient', 'Task.WhenAny(', '.WaitAsync(')) { Forbid $reader $text }
foreach ($text in @('AutomaticLookupRequiresConsentEvenWithManualSettings',
    'FailedOrFallbackAutomaticDecisionIsRejected', 'PartialAndDirectFirstAutomaticResultsAreRejected',
    'CancellationWaitsForNativeReturnAndBlocksReentry', 'TargetAndMonotonicAgeAreRequired',
    'PublicResultDoesNotExposeRawData')) { Require $tests $text }
Forbid $tests '[ModuleInitializer]'
Require $main 'WindowsRouteProxyImporterTests.RunAsync().GetAwaiter().GetResult()'
Require $bootstrap 'window.EnsureRouteProxyImportControls()'
Write-Host 'Windows proxy consent, provenance, expiry and central UI lifetime contract passed.' -ForegroundColor Green
