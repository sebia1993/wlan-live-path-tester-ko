[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$uiPath = Join-Path `
    $root `
    'src\WlanLivePathTester.App\MainWindow.RouteComparisonV3.cs'
$bootstrapPath = Join-Path `
    $root `
    'src\WlanLivePathTester.App\RouteComparisonV3Bootstrap.cs'
$rendererPath = Join-Path `
    $root `
    'src\WlanLivePathTester.Core\Reporting\InternalProxyRouteComparisonRunTextRenderer.cs'
$testPath = Join-Path `
    $root `
    'tests\WlanLivePathTester.ReportSmoke\InternalProxyRouteComparisonRunTextRendererV3Tests.cs'

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

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Condition `
        -Condition $Content.Contains($Value) `
        -Message "$Context is missing required contract text: $Value"
}

function Assert-NotContains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Condition `
        -Condition (-not $Content.Contains($Value)) `
        -Message "$Context contains forbidden contract text: $Value"
}

foreach ($path in @(
    $uiPath,
    $bootstrapPath,
    $rendererPath,
    $testPath
)) {
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
        -Message "Required route comparison UI file not found: $path"
    Assert-Condition `
        -Condition ((Get-Item -LiteralPath $path).Length -gt 0) `
        -Message "Route comparison UI file is empty: $path"
}

$ui = Get-Content -LiteralPath $uiPath -Raw
$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw
$renderer = Get-Content -LiteralPath $rendererPath -Raw
$tests = Get-Content -LiteralPath $testPath -Raw

foreach ($required in @(
    'InternalProxyRouteComparisonCoordinator',
    '.RunManualDirectiveAsync(',
    'InternalProxyRouteComparisonRunTextRenderer.Render(',
    'ReadCurrentWlanInterfaceIdV3',
    'SetRouteComparisonBusyV3(isBusy: true)',
    'SetRouteComparisonBusyV3(isBusy: false)',
    '_latestRouteComparisonRunV3 = run',
    '_routeComparisonCancellationV3?.Cancel()',
    'FindRouteComparisonDescendantV3<TabControl>'
)) {
    Assert-Contains `
        -Content $ui `
        -Value $required `
        -Context 'Route comparison WPF UI'
}

foreach ($forbidden in @(
    'LocalRouteEvidenceReader.',
    'ProxyEndpointParser.',
    'ProxyEndpointRouteAnalyzer(',
    'InternalProxyRouteComparisonEvaluator.',
    'HttpClient',
    'WebRequest',
    'WinHttp',
    'run.Message',
    'run.Limitation',
    'run.InternalRouteEvidence',
    'run.ProxyExecution'
)) {
    Assert-NotContains `
        -Content $ui `
        -Value $forbidden `
        -Context 'Route comparison WPF UI'
}

$coordinatorCallCount = [regex]::Matches(
    $ui,
    [regex]::Escape('.RunManualDirectiveAsync(')).Count
Assert-Condition `
    -Condition ($coordinatorCallCount -eq 1) `
    -Message "Route comparison UI must call RunManualDirectiveAsync exactly once. Actual: $coordinatorCallCount"

foreach ($required in @(
    '[ModuleInitializer]',
    'EventManager.RegisterClassHandler(',
    'EnsureRouteComparisonTabV3()',
    'DispatcherPriority.ContextIdle'
)) {
    Assert-Contains `
        -Content $bootstrap `
        -Value $required `
        -Context 'Route comparison UI bootstrap'
}

foreach ($required in @(
    'InternalProxyRouteComparisonRunFindingMapper.FromResult(',
    'AppendComparison(builder, result.Comparison)',
    'AppendProxyEntries(builder, result.ProxyExecution?.Analysis)',
    'SafeFingerprint(',
    'SafeCategory(',
    'SafeScheme(',
    'SafeEnum(',
    'SafeCode(',
    '입력한 내부·외부 URL과 프록시 지시문'
)) {
    Assert-Contains `
        -Content $renderer `
        -Value $required `
        -Context 'Route comparison safe renderer'
}

foreach ($forbidden in @(
    'result.Message',
    'result.Limitation',
    'comparison.Message',
    'comparison.Interpretation',
    'comparison.Limitation',
    'comparison.NextStep',
    'endpoint.EndpointLabel',
    'endpoint.Message',
    'endpoint.Warnings',
    'analysis.Message',
    'analysis.Warnings',
    'analysis.Limitation'
)) {
    Assert-NotContains `
        -Content $renderer `
        -Value $forbidden `
        -Context 'Route comparison safe renderer'
}

foreach ($required in @(
    'RendersCompletedComparisonAndOrderedProxyEntries',
    'RendersDirectAndBlockedRunsWithoutInventingEvidence',
    'SanitizesUntrustedStructuredDisplayFields',
    'DoesNotReflectFreeFormRunComparisonOrRouteText',
    'INTERNAL_PROXY_ROUTE_DIVERGED',
    'INTERNAL_PROXY_ROUTE_RUN_DIRECT_PRIMARY',
    'INTERNAL_PROXY_ROUTE_RUN_SOURCE_BLOCKED'
)) {
    Assert-Contains `
        -Content $tests `
        -Value $required `
        -Context 'Route comparison renderer tests'
}

Write-Host 'Route comparison coordinator-only UI contract passed.' `
    -ForegroundColor Green
Write-Host 'Coordinator call count: 1'
Write-Host 'Direct parser, route reader and evaluator calls in UI: 0'
Write-Host 'Free-form run, comparison and route text reads in renderer: 0'
