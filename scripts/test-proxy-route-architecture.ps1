[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src'
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "Source directory not found: $sourceRoot"
}

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

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullRoot = [System.IO.Path]::GetFullPath($root)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $prefix = $fullRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) `
        + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository root: $fullPath"
    }

    return $fullPath.Substring($prefix.Length).Replace('\', '/')
}

$sourceFiles = @(Get-ChildItem `
    -LiteralPath $sourceRoot `
    -Recurse `
    -File `
    -Filter '*.cs' |
    Sort-Object FullName)
Assert-Condition `
    -Condition ($sourceFiles.Count -gt 0) `
    -Message 'No C# source files were found.'

$sourceDocuments = @()
foreach ($file in $sourceFiles) {
    $sourceDocuments += [pscustomobject]@{
        Path = Get-NormalizedRelativePath -Path $file.FullName
        Content = Get-Content -LiteralPath $file.FullName -Raw
    }
}

$declarationContracts = @(
    [pscustomobject]@{
        Type = 'ProxyRouteDirectiveParser'
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyRouteDirectiveParser.cs'
        Pattern = '\bpublic\s+static\s+class\s+ProxyRouteDirectiveParser\b'
    },
    [pscustomobject]@{
        Type = 'ProxyDirectiveSourceSelectionPolicy'
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveSourceSelection.cs'
        Pattern = '\bpublic\s+static\s+class\s+ProxyDirectiveSourceSelectionPolicy\b'
    },
    [pscustomobject]@{
        Type = 'ProxyDirectiveRouteAnalysisPlanPolicy'
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveRouteAnalysisPlan.cs'
        Pattern = '\bpublic\s+static\s+class\s+ProxyDirectiveRouteAnalysisPlanPolicy\b'
    },
    [pscustomobject]@{
        Type = 'ProxyDirectiveRouteAnalysisExecutor'
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveRouteAnalysisExecutor.cs'
        Pattern = '\bpublic\s+static\s+class\s+ProxyDirectiveRouteAnalysisExecutor\b'
    },
    [pscustomobject]@{
        Type = 'ProxyEndpointRouteAnalyzer'
        Path = 'src/WlanLivePathTester.Windows/Routing/ProxyEndpointRouteAnalyzer.cs'
        Pattern = '\bpublic\s+sealed\s+class\s+ProxyEndpointRouteAnalyzer\b'
    },
    [pscustomobject]@{
        Type = 'ProxyDirectiveRouteBridge'
        Path = 'src/WlanLivePathTester.Windows/Routing/ProxyDirectiveRouteBridge.cs'
        Pattern = '\bpublic\s+sealed\s+class\s+ProxyDirectiveRouteBridge\b'
    },
    [pscustomobject]@{
        Type = 'InternalProxyRouteComparison'
        Path = 'src/WlanLivePathTester.Core/Routing/InternalProxyRouteComparison.cs'
        Pattern = '\bpublic\s+static\s+class\s+InternalProxyRouteComparison\b'
    },
    [pscustomobject]@{
        Type = 'InternalProxyRouteDiagnosticRunner'
        Path = 'src/WlanLivePathTester.Windows/Routing/InternalProxyRouteDiagnosticRunner.cs'
        Pattern = '\bpublic\s+sealed\s+class\s+InternalProxyRouteDiagnosticRunner\b'
    }
)

foreach ($contract in $declarationContracts) {
    $matches = @($sourceDocuments |
        Where-Object {
            [regex]::IsMatch(
                $_.Content,
                $contract.Pattern,
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        })
    $paths = @($matches | ForEach-Object { $_.Path })
    Assert-Condition `
        -Condition ($matches.Count -eq 1) `
        -Message "Expected exactly one $($contract.Type) declaration. Actual files: $($paths -join ', ')"
    Assert-Condition `
        -Condition ($matches[0].Path -ceq $contract.Path) `
        -Message "$($contract.Type) must remain in $($contract.Path), not $($matches[0].Path)."
}

$deniedDuplicatePaths = @(
    'src/WlanLivePathTester.Core/Proxy/ProxyEndpointRouteAnalysisModels.cs',
    'src/WlanLivePathTester.Core/Routing/InternalProxyRouteComparisonModels.cs'
)
foreach ($relativePath in $deniedDuplicatePaths) {
    $fullPath = Join-Path $root ($relativePath.Replace('/', '\'))
    Assert-Condition `
        -Condition (-not (Test-Path -LiteralPath $fullPath)) `
        -Message "Parallel duplicate route model file is not allowed: $relativePath"
}

$coreProxyFiles = @(
    'src/WlanLivePathTester.Core/Proxy/ProxyRouteDirectiveModels.cs',
    'src/WlanLivePathTester.Core/Proxy/ProxyRouteDirectiveParser.cs',
    'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveSourceSelection.cs',
    'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveRouteAnalysisPlan.cs',
    'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveRouteAnalysisExecutor.cs',
    'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveSourceSnapshot.cs'
)
$forbiddenCoreNetworkPatterns = @(
    '\bHttpClient\b',
    '\bHttpWebRequest\b',
    '\bWebRequest\b',
    '\bTcpClient\b',
    '\bUdpClient\b',
    '\bSocket\b',
    '\bDns\s*\.',
    '\bWinHttp[A-Za-z0-9_]*\b',
    '\bDllImport\b',
    '\bLibraryImport\b'
)
foreach ($relativePath in $coreProxyFiles) {
    $document = @($sourceDocuments |
        Where-Object { $_.Path -ceq $relativePath })
    Assert-Condition `
        -Condition ($document.Count -eq 1) `
        -Message "Required pure Core proxy source is missing: $relativePath"

    foreach ($pattern in $forbiddenCoreNetworkPatterns) {
        Assert-Condition `
            -Condition (-not [regex]::IsMatch(
                $document[0].Content,
                $pattern,
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) `
            -Message "Pure Core proxy source contains a forbidden network/native API pattern '$pattern': $relativePath"
    }
}

$privacyContracts = @(
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyRouteDirectiveModels.cs'
        Pattern = '\[JsonIgnore\]\s*public\s+string\?\s+Host\s*\{'
        Description = 'ProxyRouteDirective.Host must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveSourceSelection.cs'
        Pattern = '\[JsonIgnore\]\s*public\s+string\?\s+SelectedDirectiveText\s*\{'
        Description = 'SelectedDirectiveText must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveRouteAnalysisPlan.cs'
        Pattern = '\[JsonIgnore\]\s*public\s+string\?\s+DirectiveText\s*\{'
        Description = 'Route-analysis plan DirectiveText must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveRouteAnalysisExecutor.cs'
        Pattern = '\[JsonIgnore\]\s*public\s+TAnalysis\?\s+Analysis\s*\{'
        Description = 'Execution Analysis payload must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveSourceSnapshot.cs'
        Pattern = '\[JsonIgnore\]\s*public\s+string\?\s+TargetSpecificDirective\s*\{'
        Description = 'TargetSpecificDirective must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Proxy/ProxyDirectiveSourceSnapshot.cs'
        Pattern = '\[JsonIgnore\]\s*public\s+string\?\s+ManualProxyDirective\s*\{'
        Description = 'ManualProxyDirective must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Routing/InternalProxyRouteDiagnosticRunModels.cs'
        Pattern = '\[property:\s*JsonIgnore\]\s*DestinationRouteEvidence\?\s+InternalRouteEvidence'
        Description = 'InternalRouteEvidence must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Routing/InternalProxyRouteDiagnosticRunModels.cs'
        Pattern = '\[property:\s*JsonIgnore\]\s*ProxyEndpointRouteAnalysisResult\?\s+ProxyRouteAnalysis'
        Description = 'ProxyRouteAnalysis must remain JSON ignored.'
    },
    [pscustomobject]@{
        Path = 'src/WlanLivePathTester.Core/Routing/InternalProxyRouteDiagnosticRunModels.cs'
        Pattern = '\[property:\s*JsonIgnore\]\s*InternalProxyRouteComparisonResult\?\s+Comparison'
        Description = 'Comparison payload must remain JSON ignored.'
    }
)
foreach ($contract in $privacyContracts) {
    $document = @($sourceDocuments |
        Where-Object { $_.Path -ceq $contract.Path })
    Assert-Condition `
        -Condition ($document.Count -eq 1) `
        -Message "Privacy contract source is missing: $($contract.Path)"
    Assert-Condition `
        -Condition ([regex]::IsMatch(
            $document[0].Content,
            $contract.Pattern,
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) `
        -Message $contract.Description
}

$bridgePath = Join-Path $root `
    'src\WlanLivePathTester.Windows\Routing\ProxyDirectiveRouteBridge.cs'
$bridge = Get-Content -LiteralPath $bridgePath -Raw
Assert-Condition `
    -Condition ($bridge.Contains('ProxyEndpointParser.Parse(')) `
    -Message 'ProxyDirectiveRouteBridge must reuse ProxyEndpointParser.'
Assert-Condition `
    -Condition ($bridge.Contains('_routeAnalyzer.AnalyzeAsync(')) `
    -Message 'ProxyDirectiveRouteBridge must reuse ProxyEndpointRouteAnalyzer.'
Assert-Condition `
    -Condition (-not $bridge.Contains('LocalRouteEvidenceReader.')) `
    -Message 'ProxyDirectiveRouteBridge must not bypass the existing route analyzer.'
Assert-Condition `
    -Condition (-not $bridge.Contains('DllImport')) `
    -Message 'ProxyDirectiveRouteBridge must not add native P/Invoke.'

$runnerPath = Join-Path $root `
    'src\WlanLivePathTester.Windows\Routing\InternalProxyRouteDiagnosticRunner.cs'
$runner = Get-Content -LiteralPath $runnerPath -Raw
Assert-Condition `
    -Condition ($runner.Contains(
        'ProxyDirectiveSourceSnapshotSelectionPolicy.Select(')) `
    -Message 'Diagnostic runner must use the source snapshot selection policy.'
Assert-Condition `
    -Condition ($runner.Contains(
        'ProxyDirectiveRouteAnalysisPlanPolicy.Create(')) `
    -Message 'Diagnostic runner must evaluate the execution plan before route reads.'
Assert-Condition `
    -Condition ($runner.Contains(
        'InternalProxyRouteComparison.Compare(')) `
    -Message 'Diagnostic runner must reuse the existing route comparison engine.'
Assert-Condition `
    -Condition (-not $runner.Contains('HttpClient')) `
    -Message 'Diagnostic runner must not add HTTP traffic.'
Assert-Condition `
    -Condition (-not $runner.Contains('DllImport')) `
    -Message 'Diagnostic runner must not add native P/Invoke.'

$requiredDocuments = @(
    'docs/PROXY_ROUTE_DIRECTIVE_PARSER.md',
    'docs/PROXY_DIRECTIVE_SOURCE_SELECTION.md',
    'docs/PROXY_DIRECTIVE_SOURCE_SNAPSHOT.md',
    'docs/PROXY_DIRECTIVE_ROUTE_ANALYSIS_PLAN.md',
    'docs/PROXY_DIRECTIVE_ROUTE_ANALYSIS_EXECUTOR.md',
    'docs/PROXY_DIRECTIVE_ROUTE_BRIDGE.md',
    'docs/INTERNAL_PROXY_ROUTE_DIAGNOSTIC_RUNNER.md'
)
foreach ($relativePath in $requiredDocuments) {
    $fullPath = Join-Path $root ($relativePath.Replace('/', '\'))
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $fullPath -PathType Leaf) `
        -Message "Required proxy route architecture document is missing: $relativePath"
    Assert-Condition `
        -Condition ((Get-Item -LiteralPath $fullPath).Length -gt 0) `
        -Message "Proxy route architecture document is empty: $relativePath"
}

Write-Host 'Proxy route architecture contract passed.' `
    -ForegroundColor Green
Write-Host "C# source files inspected: $($sourceDocuments.Count)"
Write-Host "Unique pipeline declarations: $($declarationContracts.Count)"
Write-Host "Pure Core proxy files inspected: $($coreProxyFiles.Count)"
Write-Host "JSON privacy contracts: $($privacyContracts.Count)"
Write-Host "Required architecture documents: $($requiredDocuments.Count)"
