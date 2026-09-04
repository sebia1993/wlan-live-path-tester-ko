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
        throw "GitHub CLI failed: gh $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }

    $json = $output -join [Environment]::NewLine
    return $json | ConvertFrom-Json
}

function Get-RequiredJsonProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context does not expose the required '$Name' property."
    }

    return $property.Value
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
$tagPattern = '^v(?<version>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)-(?:[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))$'
$tagMatch = [regex]::Match(
    $normalizedTag,
    $tagPattern,
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
if (-not $tagMatch.Success) {
    throw "Published release verification requires a strict prerelease tag such as v0.1.0-alpha.10: $Tag"
}
$version = $tagMatch.Groups['version'].Value
$prerelease = $version.Substring($version.IndexOf('-') + 1)
foreach ($identifier in $prerelease.Split('.')) {
    $isNumeric = $identifier -match '^\d+$'
    $hasLeadingZero = $identifier.Length -gt 1 `
        -and $identifier.StartsWith(
            '0',
            [StringComparison]::Ordinal)
    if ($isNumeric -and $hasLeadingZero) {
        throw "Numeric prerelease identifiers cannot contain leading zeroes: $normalizedTag"
    }
}

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

    $releaseTag = [string](Get-RequiredJsonProperty `
        -Object $release `
        -Name 'tag_name' `
        -Context 'Release metadata')
    $isDraft = [bool](Get-RequiredJsonProperty `
        -Object $release `
        -Name 'draft' `
        -Context 'Release metadata')
    $isPrerelease = [bool](Get-RequiredJsonProperty `
        -Object $release `
        -Name 'prerelease' `
        -Context 'Release metadata')
    $remoteAssets = @((Get-RequiredJsonProperty `
        -Object $release `
        -Name 'assets' `
        -Context 'Release metadata'))

    Assert-Condition `
        -Condition ($releaseTag -ceq $normalizedTag) `
        -Message "Published release tag mismatch: $releaseTag"
    Assert-Condition `
        -Condition (-not $isDraft) `
        -Message 'Published release must not be a draft.'
    Assert-Condition `
        -Condition $isPrerelease `
        -Message 'Published release must retain prerelease=true.'

    $remoteAssetNames = @($remoteAssets |
        ForEach-Object {
            [string](Get-RequiredJsonProperty `
                -Object $_ `
                -Name 'name' `
                -Context 'Release asset metadata')
        } |
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
    foreach ($remoteAsset in $remoteAssets) {
        $name = [string](Get-RequiredJsonProperty `
            -Object $remoteAsset `
            -Name 'name' `
            -Context 'Release asset metadata')
        $digest = [string](Get-RequiredJsonProperty `
            -Object $remoteAsset `
            -Name 'digest' `
            -Context "Release asset '$name'")
        $remoteSize = [long](Get-RequiredJsonProperty `
            -Object $remoteAsset `
            -Name 'size' `
            -Context "Release asset '$name'")

        $digestMatch = [regex]::Match(
            $digest,
            '^sha256:(?<hash>[0-9a-f]{64})$',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Assert-Condition `
            -Condition $digestMatch.Success `
            -Message "GitHub did not expose a SHA-256 digest for: $name"
        Assert-Condition `
            -Condition ($localHashes[$name] -ceq `
                $digestMatch.Groups['hash'].Value) `
            -Message "Downloaded bytes differ from the GitHub digest: $name"

        $localSize = (Get-Item `
            -LiteralPath (Join-Path $resolvedDownloadRoot $name)).Length
        Assert-Condition `
            -Condition ($localSize -eq $remoteSize) `
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
        $checksumMatch = [regex]::Match(
            $line,
            '^(?<hash>[0-9a-f]{64})  (?<name>[^\\/]+)$',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $checksumMatch.Success) {
            throw "Invalid SHA256SUMS.txt line: $line"
        }

        $name = $checksumMatch.Groups['name'].Value
        Assert-Condition `
            -Condition (-not $declaredHashes.ContainsKey($name)) `
            -Message "Duplicate checksum entry: $name"
        $declaredHashes[$name] = $checksumMatch.Groups['hash'].Value
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
    $tagObject = Get-RequiredJsonProperty `
        -Object $tagRef `
        -Name 'object' `
        -Context 'Tag reference'
    $tagObjectType = [string](Get-RequiredJsonProperty `
        -Object $tagObject `
        -Name 'type' `
        -Context 'Tag reference object')
    $tagCommitSha = [string](Get-RequiredJsonProperty `
        -Object $tagObject `
        -Name 'sha' `
        -Context 'Tag reference object')
    Assert-Condition `
        -Condition ($tagObjectType -ceq 'commit') `
        -Message 'Expected a lightweight release tag pointing directly to a commit.'
    Assert-Condition `
        -Condition ($tagCommitSha -match '^[0-9a-f]{40}$') `
        -Message "Invalid release commit SHA: $tagCommitSha"

    $portablePath = Join-Path `
        $resolvedDownloadRoot `
        $portableAssetName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($portablePath)
    try {
        $entriesByNormalizedName = @{}
        foreach ($entry in $archive.Entries) {
            $normalizedName = $entry.FullName.Replace('\', '/')
            if ($entriesByNormalizedName.ContainsKey($normalizedName)) {
                throw "Published Portable ZIP contains duplicate entry: $normalizedName"
            }
            $entriesByNormalizedName[$normalizedName] = $entry
        }

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
                -Condition ($entriesByNormalizedName.ContainsKey($requiredEntry)) `
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
        foreach ($entryName in $entriesByNormalizedName.Keys) {
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

            $extension = [System.IO.Path]::GetExtension(
                $normalized).ToLowerInvariant()
            Assert-Condition `
                -Condition ($deniedExtensions -notcontains $extension) `
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
    $shouldRemove = $createdTemporaryRoot `
        -and (Test-Path -LiteralPath $resolvedDownloadRoot)
    if ($shouldRemove) {
        Remove-Item `
            -LiteralPath $resolvedDownloadRoot `
            -Recurse `
            -Force
    }
}
