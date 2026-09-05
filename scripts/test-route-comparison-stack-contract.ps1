[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) { throw $Message }
}

function Read-Source {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $path = Join-Path $root $RelativePath
    Assert-True `
        -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
        -Message "Required route-comparison source is missing: $RelativePath"
    $content = Get-Content -LiteralPath $path -Raw
    Assert-True `
        -Condition (-not [string]::IsNullOrWhiteSpace($content)) `
        -Message "Required route-comparison source is empty: $RelativePath"
    return $content
}

$required = @(
    'src\WlanLivePathTester.Core\Proxy\ProxyEndpointParser.cs',
    'src\WlanLivePathTester.Core\Routing\ProxyEndpointRouteModels.cs',
    'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparison.cs',
    'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparisonRun.cs',
    'src\WlanLivePathTester.Windows\Routing\ProxyEndpointRouteAnalyzer.cs',
    'src\WlanLivePathTester.Windows\Routing\InternalProxyRouteComparisonCoordinator.cs',
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunFindingMapper.cs',
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunSnapshotMapper.cs',
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunReportWriter.cs',
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunTextRenderer.cs',
    'src\WlanLivePathTester.App\MainWindow.RouteComparisonCoordinatorV2.cs',
    'src\WlanLivePathTester.App\RouteComparisonCoordinatorV2Bootstrap.cs'
)
foreach ($path in $required) { $null = Read-Source $path }

$parallelFiles = @(
    'src\WlanLivePathTester.Core\Proxy\ProxyRouteDirectiveModels.cs',
    'src\WlanLivePathTester.Core\Proxy\ProxyRouteDirectiveParser.cs',
    'src\WlanLivePathTester.Core\Proxy\ProxyEndpointRouteAnalysisModels.cs',
    'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparisonEvaluator.cs',
    'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparisonModels.cs'
)
foreach ($path in $parallelFiles) {
    Assert-True `
        -Condition (-not (Test-Path -LiteralPath (Join-Path $root $path))) `
        -Message "A parallel proxy/route implementation must not be introduced: $path"
}

$coordinator = Read-Source `
    'src\WlanLivePathTester.Windows\Routing\InternalProxyRouteComparisonCoordinator.cs'
$parseIndex = $coordinator.IndexOf(
    'ProxyEndpointParser.Parse', [StringComparison]::Ordinal)
$internalIndex = $coordinator.IndexOf(
    'internalRoute = await _internalRouteReader.ReadAsync',
    [StringComparison]::Ordinal)
$proxyIndex = $coordinator.IndexOf(
    'proxyAnalysis = await _proxyRouteAnalysisService',
    [StringComparison]::Ordinal)
Assert-True `
    -Condition ($parseIndex -ge 0 -and $internalIndex -ge 0 -and $proxyIndex -ge 0) `
    -Message 'The coordinator is missing a parser or route-analysis call site.'
Assert-True `
    -Condition ($parseIndex -lt $internalIndex -and $internalIndex -lt $proxyIndex) `
    -Message 'The required order is proxy parse, internal route, then proxy route.'
Assert-True `
    -Condition ($coordinator.Contains('InternalProxyRouteComparison.Compare')) `
    -Message 'The coordinator must reuse the existing comparison engine.'

$ui = Read-Source `
    'src\WlanLivePathTester.App\MainWindow.RouteComparisonCoordinatorV2.cs'
foreach ($requiredText in @(
    '_routeComparisonCoordinatorV2.RunAsync',
    'InternalProxyRouteComparisonRunTextRenderer.Render',
    'InternalProxyRouteComparisonRunReportWriter',
    'assemblyVersion is null'
)) {
    Assert-True `
        -Condition ($ui.Contains($requiredText)) `
        -Message "The WPF integration is missing: $requiredText"
}
foreach ($forbiddenText in @(
    'ProxyEndpointParser.Parse(',
    'LocalRouteEvidenceReader.ReadAsync(',
    'new ProxyEndpointRouteAnalyzer',
    'InternalProxyRouteComparison.Compare(',
    'HttpClient',
    '.Version?'
)) {
    Assert-True `
        -Condition (-not $ui.Contains($forbiddenText)) `
        -Message "The WPF integration bypasses or breaks its safe boundary: $forbiddenText"
}

$snapshot = Read-Source `
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunSnapshotMapper.cs'
foreach ($forbiddenText in @(
    'result.Message',
    'result.Limitation',
    'comparison.Message',
    'comparison.Limitation',
    'comparison.Warnings',
    'result.InternalRouteEvidence',
    'result.ProxyRouteAnalysis'
)) {
    Assert-True `
        -Condition (-not $snapshot.Contains($forbiddenText)) `
        -Message "The safe snapshot copies a raw/free-form field: $forbiddenText"
}
Assert-True `
    -Condition ($snapshot.Contains('RouteInterfaceFingerprint.DisplayLength')) `
    -Message 'The safe snapshot must enforce the short interface fingerprint length.'

$report = Read-Source `
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunReportWriter.cs'
foreach ($requiredText in @(
    'InternalProxyRouteComparisonRunSnapshotMapper',
    'Content-Security-Policy',
    'ProtectCsvFormula',
    'WriteAtomic',
    'SHA256.HashData'
)) {
    Assert-True `
        -Condition ($report.Contains($requiredText)) `
        -Message "The local report writer is missing: $requiredText"
}
foreach ($forbiddenText in @(
    'System.Net.Http',
    'HttpClient',
    'Dns.',
    'Socket(',
    'TcpClient',
    'LocalRouteEvidenceReader',
    'ProxyEndpointRouteAnalyzer',
    'WinHttp'
)) {
    Assert-True `
        -Condition (-not $report.Contains($forbiddenText)) `
        -Message "The local report writer references a network path: $forbiddenText"
}

$renderer = Read-Source `
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunTextRenderer.cs'
Assert-True `
    -Condition ($renderer.Contains('InternalProxyRouteComparisonRunSnapshotMapper')) `
    -Message 'The text renderer must consume the strict safe snapshot.'
foreach ($forbiddenText in @(
    'result.Message',
    'result.Limitation',
    'InternalRouteEvidence',
    'ProxyRouteAnalysis'
)) {
    Assert-True `
        -Condition (-not $renderer.Contains($forbiddenText)) `
        -Message "The text renderer reads a raw/free-form field: $forbiddenText"
}

Write-Host 'Route-comparison stack source contract passed.' `
    -ForegroundColor Green
Write-Host "Required source files: $($required.Count)"
Write-Host 'Parallel implementation files: absent'
Write-Host 'WPF entrypoint: coordinator only'
Write-Host 'Text and report output: strict safe snapshot only'
