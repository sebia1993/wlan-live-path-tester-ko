[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputRoot = 'artifacts\release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version must be Semantic Versioning without a leading v: $Version"
}

$numericVersion = '{0}.{1}.{2}.0' -f `
    $Matches['major'], `
    $Matches['minor'], `
    $Matches['patch']

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root `
    'src\WlanLivePathTester.App\WlanLivePathTester.App.csproj'
$resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $root $OutputRoot))
}

$workRoot = Join-Path $resolvedOutputRoot '_work'
$portablePublish = Join-Path $workRoot 'portable-publish'
$singlePublish = Join-Path $workRoot 'single-publish'
$portableStage = Join-Path $workRoot 'portable-stage'

$portableAssetName = 'WlanLivePathTester-win-x64-portable.zip'
$singleAssetName = 'WlanLivePathTester-win-x64-single-file.exe'
$checksumAssetName = 'SHA256SUMS.txt'
$noticeAssetName = 'THIRD_PARTY_NOTICES.md'

$portableAsset = Join-Path $resolvedOutputRoot $portableAssetName
$singleAsset = Join-Path $resolvedOutputRoot $singleAssetName
$checksumAsset = Join-Path $resolvedOutputRoot $checksumAssetName
$noticeAsset = Join-Path $resolvedOutputRoot $noticeAssetName

function Invoke-DotnetPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Destination,

        [Parameter(Mandatory = $true)]
        [bool]$SingleFile
    )

    $arguments = @(
        'publish',
        $project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $Destination,
        '--nologo',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:Deterministic=true',
        '-p:ContinuousIntegrationBuild=true',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$numericVersion",
        "-p:FileVersion=$numericVersion",
        "-p:InformationalVersion=$Version"
    )

    if ($SingleFile) {
        $arguments += @(
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=false'
        )
    }
    else {
        $arguments += '-p:PublishSingleFile=false'
    }

    dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for: $Destination"
    }
}

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required release file not found: $Source"
    }

    $destinationDirectory = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force |
            Out-Null
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string[]]$Lines
    )

    [System.IO.File]::WriteAllLines(
        $Path,
        $Lines,
        [System.Text.UTF8Encoding]::new($false))
}

if (Test-Path -LiteralPath $resolvedOutputRoot) {
    Remove-Item -LiteralPath $resolvedOutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force |
    Out-Null
New-Item -ItemType Directory -Path $workRoot -Force |
    Out-Null

try {
    Write-Host 'Publishing portable self-contained application...' `
        -ForegroundColor Cyan
    Invoke-DotnetPublish `
        -Destination $portablePublish `
        -SingleFile $false

    Write-Host 'Publishing self-contained single-file application...' `
        -ForegroundColor Cyan
    Invoke-DotnetPublish `
        -Destination $singlePublish `
        -SingleFile $true

    Get-ChildItem -LiteralPath $portablePublish -Recurse -Filter '*.pdb' |
        Remove-Item -Force
    Get-ChildItem -LiteralPath $singlePublish -Recurse -Filter '*.pdb' |
        Remove-Item -Force

    New-Item -ItemType Directory -Path $portableStage -Force |
        Out-Null
    Copy-Item `
        -Path (Join-Path $portablePublish '*') `
        -Destination $portableStage `
        -Recurse `
        -Force

    Copy-RequiredFile `
        -Source (Join-Path $root 'README.md') `
        -Destination (Join-Path $portableStage 'README.md')
    Copy-RequiredFile `
        -Source (Join-Path $root 'LICENSE') `
        -Destination (Join-Path $portableStage 'LICENSE')
    Copy-RequiredFile `
        -Source (Join-Path $root 'THIRD_PARTY_NOTICES.md') `
        -Destination (Join-Path $portableStage 'THIRD_PARTY_NOTICES.md')
    Copy-RequiredFile `
        -Source (Join-Path $root 'docs\NETWORK_BOUNDARY.md') `
        -Destination (Join-Path $portableStage 'docs\NETWORK_BOUNDARY.md')
    Copy-RequiredFile `
        -Source (Join-Path $root 'docs\BROWSER_OBSERVATION.md') `
        -Destination (Join-Path $portableStage 'docs\BROWSER_OBSERVATION.md')
    Copy-RequiredFile `
        -Source (Join-Path $root 'docs\REPORTING.md') `
        -Destination (Join-Path $portableStage 'docs\REPORTING.md')
    Copy-RequiredFile `
        -Source (Join-Path $root 'docs\RELEASE_VALIDATION.md') `
        -Destination (Join-Path $portableStage 'docs\RELEASE_VALIDATION.md')

    $exampleConfig = Join-Path $root 'config\targets.example.json'
    if (Test-Path -LiteralPath $exampleConfig -PathType Leaf) {
        Copy-RequiredFile `
            -Source $exampleConfig `
            -Destination (Join-Path $portableStage 'config\targets.example.json')
    }

    $sourceRevision = if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        'local-build'
    }
    else {
        $env:GITHUB_SHA
    }

    Write-Utf8NoBom `
        -Path (Join-Path $portableStage 'START_HERE.txt') `
        -Lines @(
            'WLAN Live Path Tester KO',
            '',
            '1. ZIP 파일을 먼저 완전히 압축 해제하십시오.',
            '2. WlanLivePathTester.exe를 실행하십시오.',
            '3. Python 또는 별도 .NET 설치와 관리자 권한은 필요하지 않습니다.',
            '4. 프로그램 시작만으로 외부 측정 요청을 만들지 않습니다.',
            '5. 실제 회사 환경 검증 방법은 docs\RELEASE_VALIDATION.md를 확인하십시오.',
            '6. 회사 밖으로 보고서를 공유하기 전에 마스킹 내용을 직접 재검토하십시오.'
        )

    Write-Utf8NoBom `
        -Path (Join-Path $portableStage 'BUILD_INFO.txt') `
        -Lines @(
            "Version=$Version",
            'RuntimeIdentifier=win-x64',
            'SelfContained=true',
            'PublishTrimmed=false',
            "SourceRevision=$sourceRevision",
            "BuiltAtUtc=$([DateTimeOffset]::UtcNow.ToString('O'))"
        )

    $portableExe = Join-Path $portableStage 'WlanLivePathTester.exe'
    if (-not (Test-Path -LiteralPath $portableExe -PathType Leaf)) {
        throw "Portable executable not found: $portableExe"
    }

    $publishedSingleExe = Join-Path `
        $singlePublish `
        'WlanLivePathTester.exe'
    if (-not (Test-Path -LiteralPath $publishedSingleExe -PathType Leaf)) {
        throw "Single-file executable not found: $publishedSingleExe"
    }

    Compress-Archive `
        -Path (Join-Path $portableStage '*') `
        -DestinationPath $portableAsset `
        -CompressionLevel Optimal
    Copy-Item `
        -LiteralPath $publishedSingleExe `
        -Destination $singleAsset `
        -Force
    Copy-RequiredFile `
        -Source (Join-Path $root 'THIRD_PARTY_NOTICES.md') `
        -Destination $noticeAsset

    $hashTargets = @(
        $portableAsset,
        $singleAsset,
        $noticeAsset
    )
    $hashLines = foreach ($target in $hashTargets) {
        $hash = Get-FileHash -LiteralPath $target -Algorithm SHA256
        '{0}  {1}' -f `
            $hash.Hash.ToLowerInvariant(), `
            [System.IO.Path]::GetFileName($target)
    }
    Write-Utf8NoBom -Path $checksumAsset -Lines $hashLines

    Write-Host 'Release assets created:' -ForegroundColor Green
    Get-ChildItem -LiteralPath $resolvedOutputRoot -File |
        Sort-Object Name |
        Select-Object Name, Length, LastWriteTime |
        Format-Table -AutoSize
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
