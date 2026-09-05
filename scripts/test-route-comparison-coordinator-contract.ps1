[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

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

function Read-RequiredText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $path = Join-Path $root $RelativePath
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
        -Message "Required route-comparison file not found: $RelativePath"
    $text = Get-Content -LiteralPath $path -Raw
    Assert-Condition `
        -Condition (-not [string]::IsNullOrWhiteSpace($text)) `
        -Message "Route-comparison file is empty: $RelativePath"
    return $text
}

$requiredFiles = @(
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
    'src\WlanLivePathTester.App\MainWindow.RouteComparisonCoordinator.cs',
    'src\WlanLivePathTester.App\RouteComparisonCoordinatorBootstrap.cs',
    'tests\WlanLivePathTester.WindowsSmoke\InternalProxyRouteComparisonCoordinatorTests.cs',
    'tests\WlanLivePathTester.ReportSmoke\InternalProxyRouteComparisonRunFindingMapperTests.cs',
    'tests\WlanLivePathTester.ReportSmoke\InternalProxyRouteComparisonRunSnapshotMapperTests.cs',
    'tests\WlanLivePathTester.ReportSmoke\InternalProxyRouteComparisonRunReportWriterTests.cs',
    'tests\WlanLivePathTester.ReportSmoke\InternalProxyRouteComparisonRunTextRendererTests.cs',
    'docs\INTERNAL_PROXY_ROUTE_COMPARISON_COORDINATOR.md',
    'docs\INTERNAL_PROXY_ROUTE_COMPARISON_RUN_FINDINGS.md',
    'docs\INTERNAL_PROXY_ROUTE_COMPARISON_RUN_REPORT.md',
    'docs\INTERNAL_PROXY_ROUTE_COMPARISON_COORDINATOR_UI.md'
)

foreach ($relativePath in $requiredFiles) {
    $null = Read-RequiredText -RelativePath $relativePath
}

$obsoleteParallelImplementations = @(
    'src\WlanLivePathTester.Core\Proxy\ProxyRouteDirectiveModels.cs',
    'src\WlanLivePathTester.Core\Proxy\ProxyRouteDirectiveParser.cs',
    'src\WlanLivePathTester.Core\Proxy\ProxyEndpointRouteAnalysisModels.cs',
    'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparisonEvaluator.cs',
    'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparisonModels.cs'
)
foreach ($relativePath in $obsoleteParallelImplementations) {
    Assert-Condition `
        -Condition (-not (Test-Path -LiteralPath (Join-Path $root $relativePath))) `
        -Message "Obsolete parallel proxy/route implementation must not be introduced: $relativePath"
}

$runContract = Read-RequiredText `
    -RelativePath 'src\WlanLivePathTester.Core\Routing\InternalProxyRouteComparisonRun.cs'
Assert-Condition `
    -Condition ($runContract.Contains('[property: JsonIgnore]')) `
    -Message 'Raw internal and proxy route evidence must remain JsonIgnore in the run contract.'
Assert-Condition `
    -Condition ($runContract.Contains('InternalRouteEvidence')) `
    -Message 'The in-memory internal evidence field is required for explicit local reporting.'
Assert-Condition `
    -Condition ($runContract.Contains('ProxyRouteAnalysis')) `
    -Message 'The in-memory proxy evidence field is required for explicit local reporting.'

$coordinator = Read-RequiredText `
    -RelativePath 'src\WlanLivePathTester.Windows\Routing\InternalProxyRouteComparisonCoordinator.cs'
foreach ($requiredCall in @(
    'ProxyEndpointParser.Parse',
    '_internalRouteReader.ReadAsync',
    '_proxyRouteAnalysisService',
    'InternalProxyRouteComparison.Compare'
)) {
    Assert-Condition `
        -Condition ($coordinator.Contains($requiredCall)) `
        -Message "Coordinator is missing the required existing component call: $requiredCall"
}
Assert-Condition `
    -Condition ($coordinator.IndexOf('ProxyEndpointParser.Parse', [StringComparison]::Ordinal) -lt `
        $coordinator.IndexOf('_internalRouteReader.ReadAsync', [StringComparison]::Ordinal)) `
    -Message 'Proxy input must be parsed before the internal DNS/route reader is called.'
Assert-Condition `
    -Condition ($coordinator.IndexOf('_internalRouteReader.ReadAsync', [StringComparison]::Ordinal) -lt `
        $coordinator.IndexOf('_proxyRouteAnalysisService', [StringComparison]::Ordinal)) `
    -Message 'Internal route evidence must be read before proxy endpoint route analysis.'
foreach ($forbiddenNetworkApi in @(
    'HttpClient',
    'HttpWebRequest',
    'WebRequest.Create',
    'Socket(',
    'TcpClient',
    'WinHttpOpenRequest',
    'WinHttpSendRequest'
)) {
    Assert-Condition `
        -Condition (-not $coordinator.Contains($forbiddenNetworkApi)) `
        -Message "Coordinator must not introduce a direct transport API: $forbiddenNetworkApi"
}

$ui = Read-RequiredText `
    -RelativePath 'src\WlanLivePathTester.App\MainWindow.RouteComparisonCoordinator.cs'
Assert-Condition `
    -Condition ($ui.Contains('_routeComparisonCoordinatorV1.RunAsync')) `
    -Message 'WPF must call the coordinator as the single execution entrypoint.'
Assert-Condition `
    -Condition ($ui.Contains('InternalProxyRouteComparisonRunTextRenderer.Render')) `
    -Message 'WPF must render route results through the safe snapshot renderer.'
Assert-Condition `
    -Condition ($ui.Contains('InternalProxyRouteComparisonRunReportWriter')) `
    -Message 'WPF must use the dedicated safe report writer for explicit exports.'
foreach ($forbiddenDirectCall in @(
    'ProxyEndpointParser.Parse(',
    'LocalRouteEvidenceReader.ReadAsync(',
    'ProxyEndpointRouteAnalyzer(',
    'InternalProxyRouteComparison.Compare('
)) {
    Assert-Condition `
        -Condition (-not $ui.Contains($forbiddenDirectCall)) `
        -Message "WPF must not bypass the coordinator with a direct call: $forbiddenDirectCall"
}
Assert-Condition `
    -Condition (-not $ui.Contains('HttpClient')) `
    -Message 'WPF route-comparison UI must not perform HTTP requests.'
Assert-Condition `
    -Condition (-not $ui.Contains('SelectedInterface.InterfaceIdentity')) `
    -Message 'WPF must not render a full selected-interface identity.'

$snapshotMapper = Read-RequiredText `
    -RelativePath 'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunSnapshotMapper.cs'
foreach ($requiredBoundary in @(
    'NormalizeFingerprint',
    'RouteInterfaceFingerprint.DisplayLength',
    'InternalProxyRouteComparisonRunFindingMapper',
    'SensitiveValuesIncluded: false'
)) {
    Assert-Condition `
        -Condition ($snapshotMapper.Contains($requiredBoundary)) `
        -Message "Safe snapshot mapper is missing a required boundary: $requiredBoundary"
}
foreach ($forbiddenCopy in @(
    'result.Message',
    'result.Limitation',
    'comparison.Message',
    'comparison.Limitation',
    'comparison.Warnings',
    'InternalRouteEvidence',
    'ProxyRouteAnalysis'
)) {
    Assert-Condition `
        -Condition (-not $snapshotMapper.Contains($forbiddenCopy)) `
        -Message "Safe snapshot mapper must not copy raw/free-form evidence: $forbiddenCopy"
}

$reportWriter = Read-RequiredText `
    -RelativePath 'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunReportWriter.cs'
Assert-Condition `
    -Condition ($reportWriter.Contains('InternalProxyRouteComparisonRunSnapshotMapper')) `
    -Message 'Report writer must create its document from the strict safe snapshot mapper.'
foreach ($forbiddenNetworkApi in @(
    'System.Net.Http',
    'HttpClient',
    'Dns.',
    'Socket(',
    'TcpClient',
    'LocalRouteEvidenceReader',
    'ProxyEndpointRouteAnalyzer',
    'WinHttp'
)) {
    Assert-Condition `
        -Condition (-not $reportWriter.Contains($forbiddenNetworkApi)) `
        -Message "Report writer must remain local-only and must not reference: $forbiddenNetworkApi"
}
foreach ($requiredSafety in @(
    'Content-Security-Policy',
    'ProtectCsvFormula',
    'WriteAtomic',
    'SHA256.HashData',
    'SensitiveValuesIncluded: false'
)) {
    Assert-Condition `
        -Condition ($reportWriter.Contains($requiredSafety)) `
        -Message "Report writer is missing a required safety control: $requiredSafety"
}

$textRenderer = Read-RequiredText `
    -RelativePath 'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunTextRenderer.cs'
Assert-Condition `
    -Condition ($textRenderer.Contains('InternalProxyRouteComparisonRunSnapshotMapper')) `
    -Message 'Text renderer must use the strict safe snapshot mapper.'
foreach ($forbiddenRead in @(
    'result.Message',
    'result.Limitation',
    'InternalRouteEvidence',
    'ProxyRouteAnalysis'
)) {
    Assert-Condition `
        -Condition (-not $textRenderer.Contains($forbiddenRead)) `
        -Message "Text renderer must not read raw/free-form fields: $forbiddenRead"
}

Write-Host 'Route-comparison coordinator source contract passed.' `
    -ForegroundColor Green
Write-Host "Required files: $($requiredFiles.Count)"
Write-Host 'Parallel implementation files: absent'
Write-Host 'WPF execution entrypoint: coordinator only'
Write-Host 'Report and text output: strict safe snapshot only'
