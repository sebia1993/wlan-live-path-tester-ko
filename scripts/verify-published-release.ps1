[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [string]$DownloadRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

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

function Invoke-GhJson {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed: gh $($Arguments -join ' ')`n$output"
    }

    return $output | ConvertFrom-Json
}

function Test-PeHeader {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        return $stream.ReadByte() -eq 0x4D `
            -and $stream.ReadByte() -eq 0x5A
    }
    finally {
        $stream.Dispose()
    }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (Get-FileHash `
        -LiteralPath $Path `
        -Algorithm SHA256).Hash.ToLowerInvariant()
}

$normalizedTag = $Tag.Trim()
if ($normalizedTag -notmatch '^v(?<version>(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)-[0-9A-Za-z.-]+)$') {
    throw "Published release verification requires a prerelease tag such as v0.1.0-alpha.5: $Tag"
}
$version = $Matches['version']

if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Repository must use owner/name format: $Repository"
}

$createdTemporaryRoot = [string]::IsNullOrWhiteSpace($DownloadRoot)
$resolvedDownloadRoot = if ($createdTemporaryRoot) {
    Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ('WlanLivePathTester.PublishedRelease.' `
            + [Guid]::NewGuid().ToString('N'))
}
elseif ([System.IO.Path]::IsPathRooted($DownloadRoot)) {
    [System.IO.Path]::GetFullPath($DownloadRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $DownloadRoot))
}

$portableAssetName = 'WlanLivePathTester-win-x64-portable.zip'
$singleAssetName = 'WlanLivePathTester-win-x64-single-file.exe'
$checksumAssetName = 'SHA256SUMS.txt'
$noticeAssetName = 'THIRD_PARTY_NOTICES.md'
$expectedAssetNames = @(
    $checksumAssetName,
    $noticeAssetName,
    $portableAssetName,
    $singleAssetName
) | Sort-Object

try {
    if (Test-Path -LiteralPath $resolvedDownloadRoot) {
        Remove-Item -LiteralPath $resolvedDownloadRoot -Recurse -Force
    }
    New-Item `
        -ItemType Directory `
        -Path $resolvedDownloadRoot `
        -Force | Out-Null

    Write-Host "Reading published release metadata for $normalizedTag..." `
        -ForegroundColor Cyan
    $release = Invoke-GhJson -Arguments @(
        'api',
        "repos/$Repository/releases/tags/$normalizedTag"
    )

    Assert-Condition `
        -Condition ([string]$release.tag_name -ceq $normalizedTag) `
        -Message "Published release tag mismatch: $($release.tag_name)"
    Assert-Condition `
        -Condition (-not [bool]$release.draft) `
        -Message 'Published release must not be a draft.'
    Assert-Condition `
        -Condition ([bool]$release.prerelease) `
        -Message 'Published release must retain prerelease=true.'

    $remoteAssetNames = @($release.assets |
        ForEach-Object { [string]$_.name } |
        Sort-Object)
    Assert-Condition `
        -Condition (($remoteAssetNames -join '|') -ceq `
            ($expectedAssetNames -join '|')) `
        -Message "Published release must contain exactly four approved assets. Actual: $($remoteAssetNames -join ', ')"

    Write-Host 'Downloading the published assets again...' `
        -ForegroundColor Cyan
    & gh release download $normalizedTag `
        --repo $Repository `
        --dir $resolvedDownloadRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to download all assets for $normalizedTag."
    }

    $localAssetNames = @(Get-ChildItem `
        -LiteralPath $resolvedDownloadRoot `
        -File |
        Select-Object -ExpandProperty Name |
        Sort-Object)
    Assert-Condition `
        -Condition (($localAssetNames -join '|') -ceq `
            ($expectedAssetNames -join '|')) `
        -Message "Downloaded file inventory differs from the published release. Actual: $($localAssetNames -join ', ')"

    $localHashes = @{}
    foreach ($assetName in $expectedAssetNames) {
        $assetPath = Join-Path $resolvedDownloadRoot $assetName
        Assert-Condition `
            -Condition (Test-Path -LiteralPath $assetPath -PathType Leaf) `
            -Message "Downloaded asset not found: $assetName"
        Assert-Condition `
            -Condition ((Get-Item -LiteralPath $assetPath).Length -gt 0) `
            -Message "Downloaded asset is empty: $assetName"
        $localHashes[$assetName] = Get-Sha256 -Path $assetPath
    }

    Write-Host 'Comparing downloaded bytes with GitHub asset digests...' `
        -ForegroundColor Cyan
    foreach ($remoteAsset in $release.assets) {
        $name = [string]$remoteAsset.name
        $digest = [string]$remoteAsset.digest
        Assert-Condition `
            -Condition ($digest -match '^sha256:(?<hash>[0-9a-f]{64})$') `
            -Message "GitHub did not expose a SHA-256 digest for: $name"
        Assert-Condition `
            -Condition ($localHashes[$name] -ceq $Matches['hash']) `
            -Message "Downloaded bytes differ from the GitHub digest: $name"
        Assert-Condition `
            -Condition ((Get-Item `
                -LiteralPath (Join-Path $resolvedDownloadRoot $name)).Length `
                -eq [long]$remoteAsset.size) `
            -Message "Downloaded size differs from GitHub metadata: $name"
    }

    $checksumPath = Join-Path `
        $resolvedDownloadRoot `
        $checksumAssetName
    $checksumLines = @(Get-Content -LiteralPath $checksumPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-Condition `
        -Condition ($checksumLines.Count -eq 3) `
        -Message 'SHA256SUMS.txt must contain exactly three asset hashes.'

    $declaredHashes = @{}
    foreach ($line in $checksumLines) {
        if ($line -notmatch '^(?<hash>[0-9a-f]{64})  (?<name>[^\\/]+)$') {
            throw "Invalid SHA256SUMS.txt line: $line"
        }

        $name = $Matches['name']
        Assert-Condition `
            -Condition (-not $declaredHashes.ContainsKey($name)) `
            -Message "Duplicate checksum entry: $name"
        $declaredHashes[$name] = $Matches['hash']
    }

    foreach ($assetName in @(
        $portableAssetName,
        $singleAssetName,
        $noticeAssetName
    )) {
        Assert-Condition `
            -Condition ($declaredHashes.ContainsKey($assetName)) `
            -Message "SHA256SUMS.txt is missing: $assetName"
        Assert-Condition `
            -Condition ($declaredHashes[$assetName] -ceq `
                $localHashes[$assetName]) `
            -Message "SHA256SUMS.txt mismatch after publication: $assetName"
    }

    $tagRef = Invoke-GhJson -Arguments @(
        'api',
        "repos/$Repository/git/ref/tags/$normalizedTag"
    )
    Assert-Condition `
        -Condition ([string]$tagRef.object.type -ceq 'commit') `
        -Message 'Expected a lightweight release tag pointing directly to a commit.'
    $tagCommitSha = [string]$tagRef.object.sha
    Assert-Condition `
        -Condition ($tagCommitSha -match '^[0-9a-f]{40}$') `
        -Message "Invalid release commit SHA: $tagCommitSha"

    $portablePath = Join-Path `
        $resolvedDownloadRoot `
        $portableAssetName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($portablePath)
    try {
        $entryNames = @($archive.Entries |
            ForEach-Object { $_.FullName.Replace('\', '/') })
        $duplicates = @($entryNames |
            Group-Object |
            Where-Object { $_.Count -gt 1 })
        Assert-Condition `
            -Condition ($duplicates.Count -eq 0) `
            -Message "Published Portable ZIP contains duplicate entries: $($duplicates.Name -join ', ')"

        $requiredEntries = @(
            'WlanLivePathTester.exe',
            'WlanLivePathTester.dll',
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
                -Message "Published Portable ZIP is missing: $requiredEntry"
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
                -Message 'Published Portable ZIP contains an empty entry name.'
            Assert-Condition `
                -Condition (-not $normalized.StartsWith('/')) `
                -Message "Published Portable ZIP contains an absolute path: $entryName"
            Assert-Condition `
                -Condition ($normalized -notmatch '(^|/)\.\.(/|$)') `
                -Message "Published Portable ZIP contains path traversal: $entryName"
            Assert-Condition `
                -Condition ($normalized -notmatch '^[A-Za-z]:') `
                -Message "Published Portable ZIP contains a drive path: $entryName"
            Assert-Condition `
                -Condition ($deniedExtensions -notcontains `
                    [System.IO.Path]::GetExtension($normalized).ToLowerInvariant()) `
                -Message "Published Portable ZIP contains a prohibited file: $entryName"
            Assert-Condition `
                -Condition ($normalized -notmatch `
                    '(?i)^(results|reports|logs|captures)/') `
                -Message "Published Portable ZIP contains a prohibited data directory: $entryName"
            Assert-Condition `
                -Condition ($normalized -notmatch `
                    '(?i)^config/(targets\.json|.+\.local\.json)$') `
                -Message "Published Portable ZIP contains an actual/local target configuration: $entryName"
        }
    }
    finally {
        $archive.Dispose()
    }

    $extractRoot = Join-Path $resolvedDownloadRoot '_extracted'
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        $portablePath,
        $extractRoot)
    $portableExe = Join-Path $extractRoot 'WlanLivePathTester.exe'
    Assert-Condition `
        -Condition (Test-PeHeader -Path $portableExe) `
        -Message 'Published Portable executable does not have an MZ header.'

    $buildInfoPath = Join-Path $extractRoot 'BUILD_INFO.txt'
    $buildInfo = Get-Content -LiteralPath $buildInfoPath -Raw
    Assert-Condition `
        -Condition ($buildInfo.Contains("Version=$version")) `
        -Message 'Published BUILD_INFO.txt version does not match the tag.'
    Assert-Condition `
        -Condition ($buildInfo.Contains("SourceRevision=$tagCommitSha")) `
        -Message 'Published BUILD_INFO.txt source revision does not match the tag commit.'
    Assert-Condition `
        -Condition ($buildInfo.Contains('RuntimeIdentifier=win-x64')) `
        -Message 'Published BUILD_INFO.txt does not identify win-x64.'
    Assert-Condition `
        -Condition ($buildInfo.Contains('SelfContained=true')) `
        -Message 'Published BUILD_INFO.txt does not identify a self-contained build.'

    $singlePath = Join-Path `
        $resolvedDownloadRoot `
        $singleAssetName
    Assert-Condition `
        -Condition (Test-PeHeader -Path $singlePath) `
        -Message 'Published single-file executable does not have an MZ header.'
    $productVersion = (Get-Item `
        -LiteralPath $singlePath).VersionInfo.ProductVersion
    Assert-Condition `
        -Condition (-not [string]::IsNullOrWhiteSpace($productVersion)) `
        -Message 'Published single-file executable has no ProductVersion.'
    Assert-Condition `
        -Condition ($productVersion.StartsWith(
            $version,
            [StringComparison]::OrdinalIgnoreCase)) `
        -Message "Published ProductVersion '$productVersion' does not start with '$version'."

    $signature = Get-AuthenticodeSignature -LiteralPath $singlePath
    Write-Host "Authenticode status: $($signature.Status)" `
        -ForegroundColor Yellow
    Write-Host "Published release verification passed: $normalizedTag" `
        -ForegroundColor Green
    Write-Host "Tag commit: $tagCommitSha"
    Write-Host "Portable SHA-256: $($localHashes[$portableAssetName])"
    Write-Host "Single EXE SHA-256: $($localHashes[$singleAssetName])"
}
finally {
    if ($createdTemporaryRoot `
        -and (Test-Path -LiteralPath $resolvedDownloadRoot)) {
        Remove-Item `
            -LiteralPath $resolvedDownloadRoot `
            -Recurse `
            -Force
    }
}
