[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputRoot = 'artifacts\release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
}

$portableAssetName = 'WlanLivePathTester-win-x64-portable.zip'
$singleAssetName = 'WlanLivePathTester-win-x64-single-file.exe'
$checksumAssetName = 'SHA256SUMS.txt'
$noticeAssetName = 'THIRD_PARTY_NOTICES.md'

$portableAsset = Join-Path $resolvedOutputRoot $portableAssetName
$singleAsset = Join-Path $resolvedOutputRoot $singleAssetName
$checksumAsset = Join-Path $resolvedOutputRoot $checksumAssetName
$noticeAsset = Join-Path $resolvedOutputRoot $noticeAssetName

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

function Test-PeHeader {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $first = $stream.ReadByte()
        $second = $stream.ReadByte()
        return $first -eq 0x4D -and $second -eq 0x5A
    }
    finally {
        $stream.Dispose()
    }
}

foreach ($path in @(
    $portableAsset,
    $singleAsset,
    $checksumAsset,
    $noticeAsset
)) {
    Assert-Condition `
        -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
        -Message "Required release asset not found: $path"
}

$actualAssets = @(Get-ChildItem -LiteralPath $resolvedOutputRoot -File |
    Select-Object -ExpandProperty Name |
    Sort-Object)
$expectedAssets = @(
    $checksumAssetName,
    $noticeAssetName,
    $portableAssetName,
    $singleAssetName
) | Sort-Object
Assert-Condition `
    -Condition (($actualAssets -join '|') -eq ($expectedAssets -join '|')) `
    -Message "Release output must contain exactly four assets. Actual: $($actualAssets -join ', ')"

Assert-Condition `
    -Condition ((Get-Item -LiteralPath $portableAsset).Length -gt 10MB) `
    -Message 'Portable ZIP is unexpectedly small.'
Assert-Condition `
    -Condition ((Get-Item -LiteralPath $singleAsset).Length -gt 10MB) `
    -Message 'Single-file executable is unexpectedly small.'
Assert-Condition `
    -Condition (Test-PeHeader -Path $singleAsset) `
    -Message 'Single-file asset does not have a Windows PE MZ header.'

$checksumLines = @(Get-Content -LiteralPath $checksumAsset |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
Assert-Condition `
    -Condition ($checksumLines.Count -eq 3) `
    -Message 'SHA256SUMS.txt must contain exactly three asset hashes.'

$parsedHashes = @{}
foreach ($line in $checksumLines) {
    if ($line -notmatch '^(?<hash>[0-9a-f]{64})  (?<name>[^\\/]+)$') {
        throw "Invalid SHA256SUMS.txt line: $line"
    }

    $name = $Matches['name']
    Assert-Condition `
        -Condition (-not ($parsedHashes.ContainsKey($name))) `
        -Message "Duplicate checksum entry: $name"
    $parsedHashes[$name] = $Matches['hash']
}

foreach ($assetName in @(
    $portableAssetName,
    $singleAssetName,
    $noticeAssetName
)) {
    Assert-Condition `
        -Condition ($parsedHashes.ContainsKey($assetName)) `
        -Message "Missing checksum for: $assetName"

    $assetPath = Join-Path $resolvedOutputRoot $assetName
    $actualHash = (Get-FileHash `
        -LiteralPath $assetPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition `
        -Condition ($actualHash -eq $parsedHashes[$assetName]) `
        -Message "Checksum mismatch: $assetName"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($portableAsset)
try {
    $entryNames = @($archive.Entries |
        ForEach-Object { $_.FullName.Replace('\', '/') })

    $duplicates = @($entryNames |
        Group-Object |
        Where-Object { $_.Count -gt 1 })
    $duplicateNames = @($duplicates |
        ForEach-Object { $_.Name })
    Assert-Condition `
        -Condition ($duplicates.Count -eq 0) `
        -Message "Portable ZIP contains duplicate entries: $($duplicateNames -join ', ')"

    $requiredEntries = @(
        'WlanLivePathTester.exe',
        'WlanLivePathTester.dll',
        'WlanLivePathTester.deps.json',
        'WlanLivePathTester.runtimeconfig.json',
        'coreclr.dll',
        'hostfxr.dll',
        'START_HERE.txt',
        'BUILD_INFO.txt',
        'README.md',
        'LICENSE',
        'THIRD_PARTY_NOTICES.md',
        'docs/NETWORK_BOUNDARY.md',
        'docs/DOWNLOAD_MEASUREMENT.md',
        'docs/BROWSER_OBSERVATION.md',
        'docs/NETWORK_INTERFACE_CONTEXT.md',
        'docs/WLAN_INTERFACE_CORRELATION.md',
        'docs/NETWORK_ADAPTER_DIAGNOSTICS.md',
        'docs/PROXY_ROUTE_RESOLUTION.md',
        'docs/REPORTING.md',
        'docs/TARGET_CONFIGURATION.md',
        'docs/ADMINISTRATOR_POLICY_VALIDATION.md',
        'docs/REPEATED_MEASUREMENT.md',
        'docs/RELEASE_VALIDATION.md'
    )
    foreach ($requiredEntry in $requiredEntries) {
        Assert-Condition `
            -Condition ($entryNames -contains $requiredEntry) `
            -Message "Portable ZIP is missing: $requiredEntry"
    }

    $deniedExtensions = @(
        '.pdb',
        '.pcap',
        '.pcapng',
        '.etl',
        '.evtx',
        '.har',
        '.dmp'
    )
    foreach ($entryName in $entryNames) {
        $normalized = $entryName.Trim()
        Assert-Condition `
            -Condition (-not [string]::IsNullOrWhiteSpace($normalized)) `
            -Message 'Portable ZIP contains an empty entry name.'
        Assert-Condition `
            -Condition (-not $normalized.StartsWith('/')) `
            -Message "Portable ZIP contains an absolute path: $entryName"
        Assert-Condition `
            -Condition ($normalized -notmatch '(^|/)\.\.(/|$)') `
            -Message "Portable ZIP contains path traversal: $entryName"
        Assert-Condition `
            -Condition ($normalized -notmatch '^[A-Za-z]:') `
            -Message "Portable ZIP contains a drive-qualified path: $entryName"

        $extension = [System.IO.Path]::GetExtension($normalized).ToLowerInvariant()
        Assert-Condition `
            -Condition ($deniedExtensions -notcontains $extension) `
            -Message "Portable ZIP contains a prohibited file: $entryName"

        Assert-Condition `
            -Condition ($normalized -notmatch '(?i)^(results|reports|logs|captures)/') `
            -Message "Portable ZIP contains a prohibited data directory: $entryName"
        Assert-Condition `
            -Condition ($normalized -notmatch '(?i)^config/(targets\.json|.+\.local\.json)$') `
            -Message "Portable ZIP contains an actual/local target configuration: $entryName"
    }
}
finally {
    $archive.Dispose()
}

$extractRoot = Join-Path `
    ([System.IO.Path]::GetTempPath()) `
    ('WlanLivePathTester.ReleaseSmoke.' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extractRoot -Force |
    Out-Null
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        $portableAsset,
        $extractRoot)

    $portableExe = Join-Path $extractRoot 'WlanLivePathTester.exe'
    Assert-Condition `
        -Condition (Test-PeHeader -Path $portableExe) `
        -Message 'Extracted portable executable does not have an MZ header.'

    $buildInfo = Get-Content `
        -LiteralPath (Join-Path $extractRoot 'BUILD_INFO.txt') `
        -Raw
    Assert-Condition `
        -Condition ($buildInfo.Contains("Version=$Version")) `
        -Message 'BUILD_INFO.txt does not contain the requested version.'
    Assert-Condition `
        -Condition ($buildInfo.Contains('RuntimeIdentifier=win-x64')) `
        -Message 'BUILD_INFO.txt does not identify win-x64.'
    Assert-Condition `
        -Condition ($buildInfo.Contains('SelfContained=true')) `
        -Message 'BUILD_INFO.txt does not identify a self-contained build.'
}
finally {
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
}

$productVersion = (Get-Item -LiteralPath $singleAsset).VersionInfo.ProductVersion
Assert-Condition `
    -Condition (-not [string]::IsNullOrWhiteSpace($productVersion)) `
    -Message 'Single-file executable does not expose a ProductVersion.'
$versionMatches = $productVersion.StartsWith(
    $Version,
    [StringComparison]::OrdinalIgnoreCase)
Assert-Condition `
    -Condition $versionMatches `
    -Message "Single-file ProductVersion '$productVersion' does not start with '$Version'."

$signature = Get-AuthenticodeSignature -LiteralPath $singleAsset
Write-Host "Authenticode status: $($signature.Status)" -ForegroundColor Yellow
Write-Host 'Release package validation passed.' -ForegroundColor Green
