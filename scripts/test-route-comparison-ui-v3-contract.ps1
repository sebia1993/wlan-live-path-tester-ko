[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Read-Source([string]$Path) {
    $full = Join-Path $root $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf) -or (Get-Item -LiteralPath $full).Length -eq 0) {
        throw "Required nonempty source missing: $Path"
    }
    Get-Content -LiteralPath $full -Raw -Encoding UTF8
}
function Require([string]$Source, [string]$Text) {
    if (-not $Source.Contains($Text)) { throw "Missing route coordinator/renderer contract: $Text" }
}
function Forbid([string]$Source, [string]$Text) {
    if ($Source.Contains($Text)) { throw "Forbidden route coordinator/renderer contract: $Text" }
}
$ui = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteComparisonV3.cs'
$lifetime = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteOperation.cs'
$bootstrap = Read-Source 'src\WlanLivePathTester.App\RouteComparisonV3Bootstrap.cs'
$renderer = Read-Source 'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunTextRenderer.cs'
$tests = Read-Source 'tests\WlanLivePathTester.ReportSmoke\InternalProxyRouteComparisonRunTextRendererV3Tests.cs'
foreach ($text in @('InternalProxyRouteComparisonCoordinator', '.RunManualDirectiveAsync(',
    'InternalProxyRouteComparisonRunTextRenderer.Render(', 'ReadCurrentWlanInterfaceIdV3',
    '_latestRouteComparisonRunV3 = run', 'RunRouteUiOperationAsync(', 'RequestRouteOperationCancellation()',
    '_routeComparisonProxyDirectiveV3?.Text ?? string.Empty')) { Require $ui $text }
Require $lifetime 'SetRouteComparisonBusyV3(isBusy: busy)'
Require $lifetime 'RunLocalDiagnosticAsync('
Require $lifetime 'CancelLocalDiagnostic(kind)'
foreach ($text in @('LocalRouteEvidenceReader.', 'ProxyEndpointParser.', 'ProxyEndpointRouteAnalyzer(',
    'InternalProxyRouteComparisonEvaluator.', 'HttpClient', 'WebRequest', 'WinHttp',
    'run.Message', 'run.Limitation', 'run.InternalRouteEvidence', 'run.ProxyExecution')) { Forbid $ui $text }
if ([regex]::Matches($ui, [regex]::Escape('.RunManualDirectiveAsync(')).Count -ne 1) {
    throw 'Expected exactly one production manual coordinator invocation.'
}
foreach ($text in @('[ModuleInitializer]', 'EventManager.RegisterClassHandler(',
    'EnsureRouteComparisonTabV3()', 'DispatcherPriority.ContextIdle')) { Require $bootstrap $text }
foreach ($text in @('InternalProxyRouteComparisonRunFindingMapper.FromResult(',
    'AppendComparison(builder, result.Comparison)', 'AppendProxyEntries(builder, result.ProxyExecution?.Analysis)',
    'SafeFingerprint(', 'SafeCategory(', 'SafeScheme(', 'SafeEnum(', 'SafeCode(',
    'SafeFixedNarrative(finding.NextStep)', 'return builder.ToString().TrimEnd()')) { Require $renderer $text }
foreach ($text in @('result.Message', 'result.Limitation', 'comparison.Message', 'comparison.Interpretation',
    'comparison.Limitation', 'comparison.NextStep', 'endpoint.EndpointLabel', 'endpoint.Message',
    'endpoint.Warnings', 'analysis.Message', 'analysis.Warnings', 'analysis.Limitation')) { Forbid $renderer $text }
foreach ($text in @('RendersCompletedComparisonAndOrderedProxyEntries',
    'RendersDirectAndBlockedRunsWithoutInventingEvidence', 'SanitizesUntrustedStructuredDisplayFields',
    'DoesNotReflectFreeFormRunComparisonOrRouteText', 'INTERNAL_PROXY_ROUTE_DIVERGED',
    'INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY', 'INTERNAL_PROXY_ROUTE_RUN_SOURCE_BLOCKED')) { Require $tests $text }
Write-Host 'Route coordinator-only UI and privacy renderer contracts passed.' -ForegroundColor Green
