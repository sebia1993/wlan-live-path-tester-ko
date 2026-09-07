[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required raw-input wiring file not found: $relative"
    }
    Get-Content -LiteralPath $path -Raw -Encoding UTF8
}
function Require([string]$source, [string]$text, [string]$name) {
    if (-not $source.Contains($text)) { throw "Missing raw-input wiring: $name" }
}
function Forbid([string]$source, [string]$text, [string]$name) {
    if ($source.Contains($text)) { throw "Forbidden raw-input normalization: $name" }
}

$main = Read-Source 'src\WlanLivePathTester.App\MainWindow.xaml.cs'
$repeat = Read-Source 'src\WlanLivePathTester.App\MainWindow.RepeatedMeasurement.cs'
$route = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteEvidence.cs'
$compare = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteComparisonV3.cs'
$import = Read-Source 'src\WlanLivePathTester.App\MainWindow.RouteProxyImport.cs'
$config = Read-Source 'src\WlanLivePathTester.App\MainWindow.TargetConfiguration.cs'
$reader = Read-Source 'src\WlanLivePathTester.Windows\Routing\LocalRouteEvidenceReader.cs'
$comparison = Read-Source 'src\WlanLivePathTester.Windows\Routing\InternalProxyRouteComparisonCoordinator.cs'
$proxyCoordinator = Read-Source 'src\WlanLivePathTester.Windows\Routing\ProxyDirectiveRouteAnalysisCoordinator.cs'
$resolver = Read-Source 'src\WlanLivePathTester.Windows\Proxy\ProxyRouteResolver.cs'
$importer = Read-Source 'src\WlanLivePathTester.Windows\Proxy\WindowsRouteProxyImporter.cs'

Require $main 'string url = ProxyTargetUrlTextBox.Text;' 'proxy route UI forwards raw text'
Require $main 'NetworkInputBoundary.TryHttpUri(' 'single download uses shared HTTP boundary'
Require $main 'InternalTargetUrlTextBox.Text,' 'single internal input is validated from TextBox raw value'
Require $main 'NetworkInputBoundary.TryHttpUrlLines(' 'single external list uses shared list boundary'
Require $main 'ExternalTargetUrlsTextBox.Text,' 'single external list is validated from TextBox raw value'
Forbid $main 'ProxyTargetUrlTextBox.Text.Trim()' 'proxy route UI Trim'
Forbid $main 'InternalTargetUrlTextBox.Text.Trim()' 'single internal URL Trim'
Forbid $main 'StringSplitOptions.TrimEntries' 'single external URL TrimEntries'

Require $repeat 'NetworkInputBoundary.TryHttpUri(InternalTargetUrlTextBox.Text' 'repeat internal raw boundary'
Require $repeat 'NetworkInputBoundary.TryHttpUrlLines(ExternalTargetUrlsTextBox.Text' 'repeat external raw list boundary'
Forbid $repeat 'InternalTargetUrlTextBox.Text.Trim()' 'repeat internal URL Trim'
Forbid $repeat 'StringSplitOptions.TrimEntries' 'repeat external URL TrimEntries'

Require $route '_routeEvidenceTargetTextBox.Text = InternalTargetUrlTextBox.Text;' 'route copy preserves internal raw value'
Require $route 'NetworkInputBoundary.TryHttpUrlLines(ExternalTargetUrlsTextBox.Text' 'route external copy validates raw list'
Require $route 'string target = _routeEvidenceTargetTextBox.Text;' 'route reader receives raw analysis target'
Forbid $route '_routeEvidenceTargetTextBox.Text.Trim()' 'route analysis target Trim'
Forbid $route 'InternalTargetUrlTextBox.Text.Trim()' 'route internal source Trim'
Forbid $route 'StringSplitOptions.TrimEntries' 'route external source TrimEntries'

Require $compare '_routeComparisonInternalTargetV3?.Text ?? string.Empty' 'manual comparison preserves internal raw value'
Require $compare '_routeComparisonExternalTargetV3?.Text ?? string.Empty' 'manual comparison captures external raw value'
Require $compare 'NetworkInputBoundary.TryHttpUri(externalRaw' 'manual comparison validates external raw URL'
Require $compare '_routeComparisonProxyDirectiveV3?.Text ?? string.Empty' 'manual comparison preserves proxy directive raw value'
Forbid $compare '_routeComparisonInternalTargetV3?.Text.Trim()' 'manual comparison internal Trim'
Forbid $compare '_routeComparisonExternalTargetV3?.Text.Trim()' 'manual comparison external Trim'
Forbid $compare '_routeComparisonProxyDirectiveV3?.Text.Trim()' 'manual comparison proxy Trim'

Require $import 'NetworkInputBoundary.TryHttpUri(raw' 'Windows proxy import validates original UI text'
Require $import '_routeComparisonInternalTargetV3?.Text ?? string.Empty' 'imported comparison preserves internal raw target'
Forbid $import '_routeComparisonExternalTargetV3?.Text.Trim()' 'proxy import target Trim'
Forbid $import '_routeComparisonInternalTargetV3?.Text.Trim()' 'imported comparison internal Trim'

Require $config 'TargetConfigurationFileReader.ReadStrictUtf8(configurationPath)' 'approved target UI uses bounded strict file reader'
Forbid $config 'File.ReadAllText(' 'unbounded approved target file read'
Forbid $config 'exception.Message' 'configuration error reflects arbitrary payload/path'

Require $reader 'NetworkInputBoundary.TryRouteHost(' 'local route reader validates raw host'
Forbid $reader '(target ?? string.Empty).Trim()' 'local route reader pre-Trim'
Require $comparison 'string rawInternalTarget = internalTarget ?? string.Empty;' 'comparison coordinator keeps raw internal target'
Require $comparison 'NetworkInputBoundary.TryRouteHost(internalTarget' 'comparison coordinator validates raw internal target'
Require $comparison 'NetworkInputBoundary.IsHttpUri(externalTarget' 'comparison coordinator revalidates external OriginalString'
Forbid $comparison '(internalTarget ?? string.Empty).Trim()' 'comparison coordinator pre-Trim'
Require $proxyCoordinator 'NetworkInputBoundary.IsHttpUri(targetUri' 'proxy route analysis coordinator revalidates OriginalString'
Require $resolver 'NetworkInputBoundary.TryHttpUri(' 'direct proxy resolver validates raw target/PAC'
Require $importer 'NetworkInputBoundary.IsHttpUri(target' 'Windows proxy importer revalidates target OriginalString'

Write-Host 'Raw network input wiring contract passed.' -ForegroundColor Green
Write-Host 'Checked single/repeated downloads, route evidence, route comparison/import, bounded config read and Windows coordinators.'
