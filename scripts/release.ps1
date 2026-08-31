param(
    [string]$Version,
    [ValidateSet("win-x64")][string]$Runtime = "win-x64",
    [switch]$SkipInstaller,
    [switch]$RequireModel,
    [string]$FfmpegSourceDirectory,
    [string]$SigningCertificateThumbprint,
    [string]$TimestampUrl
)

$ErrorActionPreference = "Stop"
$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$propsPath = Join-Path $workspace "Directory.Build.props"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath $propsPath
    $Version = [string]$props.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Release version must contain exactly three numeric components, for example 1.0.0."
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace "artifacts"))
$releaseDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "release\$Version"))
$allowedPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $releaseDirectory.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a release path outside the workspace artifacts directory: $releaseDirectory"
}
if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}

$publishDirectory = Join-Path $releaseDirectory "publish"
$installerDirectory = Join-Path $releaseDirectory "installer"
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

$project = Join-Path $workspace "gui\GeminiWatermarkRemover.csproj"
dotnet publish $project -c Release -r $Runtime --self-contained true -o $publishDirectory `
    -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
    -p:InformationalVersion=$Version -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Release publish failed with exit code $LASTEXITCODE." }

function Get-SignToolPath {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter "signtool.exe" -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.DirectoryName -like "*\x64" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    throw "signtool.exe was not found. Install the Windows SDK signing tools."
}

function Invoke-AuthenticodeSign([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) { return }
    if ([string]::IsNullOrWhiteSpace($TimestampUrl) -or
        -not [Uri]::IsWellFormedUriString($TimestampUrl, [UriKind]::Absolute)) {
        throw "A valid RFC 3161 TimestampUrl is required when signing."
    }
    $signTool = Get-SignToolPath
    & $signTool sign /sha1 $SigningCertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $Path." }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode verification failed for $Path`: $($signature.StatusMessage)"
    }
}

# Some native runtime packages include development symbols/static import libraries even when
# application symbols are disabled. They are not required at runtime and materially bloat releases.
Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Extension -in @(".pdb", ".lib", ".obj") } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

$modelSource = Join-Path $workspace "gui\models\lama_fp32.onnx"
$requireCompletePayload = $RequireModel -or -not $SkipInstaller
if (Test-Path -LiteralPath $modelSource -PathType Leaf) {
    $modelDirectory = Join-Path $publishDirectory "models"
    New-Item -ItemType Directory -Path $modelDirectory -Force | Out-Null
    Copy-Item -LiteralPath $modelSource -Destination (Join-Path $modelDirectory "lama_fp32.onnx")
} elseif ($requireCompletePayload) {
    throw "Installers must be complete, but gui\models\lama_fp32.onnx is unavailable. Use -SkipInstaller only for source/CI validation."
}

if ([string]::IsNullOrWhiteSpace($FfmpegSourceDirectory)) {
    $FfmpegSourceDirectory = Join-Path $workspace "third_party\ffmpeg"
    & (Join-Path $PSScriptRoot "prepare-ffmpeg.ps1") -DestinationDirectory $FfmpegSourceDirectory
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg preparation failed with exit code $LASTEXITCODE." }
}
$ffmpegSource = [System.IO.Path]::GetFullPath($FfmpegSourceDirectory)
$ffmpegRequired = @("ffmpeg.exe", "ffprobe.exe", "LICENSE.txt", "SOURCE.txt", "BUILD-README.txt")
foreach ($fileName in $ffmpegRequired) {
    $candidate = Join-Path $ffmpegSource $fileName
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "A self-contained release requires $fileName in $ffmpegSource."
    }
}
$ffmpegDestination = Join-Path $publishDirectory "tools\ffmpeg"
New-Item -ItemType Directory -Path $ffmpegDestination -Force | Out-Null
foreach ($fileName in $ffmpegRequired) {
    Copy-Item -LiteralPath (Join-Path $ffmpegSource $fileName) -Destination (Join-Path $ffmpegDestination $fileName)
}

Invoke-AuthenticodeSign (Join-Path $publishDirectory "SoftcurseMediaLabAI.exe")
Invoke-AuthenticodeSign (Join-Path $publishDirectory "SoftcurseMediaLabAI.dll")

& (Join-Path $PSScriptRoot "validate-package.ps1") `
    -PublishDirectory $publishDirectory -Version $Version -RequireModel:$requireCompletePayload `
    -RequireSignature:([bool]$SigningCertificateThumbprint) -RequireFfmpeg

if (-not $SkipInstaller) {
    $iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $isccCandidates = @(
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
        )
        $installedIscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($installedIscc) { $iscc = Get-Item -LiteralPath $installedIscc }
    }
    if (-not $iscc) {
        throw "Inno Setup 6 or 7 was not found. Install it or use -SkipInstaller for publish-only validation."
    }
    $isccPath = if ($iscc -is [System.IO.FileInfo]) { $iscc.FullName } else { $iscc.Source }
    $iss = Join-Path $workspace "media.iss"
    & $isccPath "/DMyAppVersion=$Version" "/DMyPublishDir=$publishDirectory" "/O$installerDirectory" $iss
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }
    $installerPath = Join-Path $installerDirectory "SoftcurseMediaLabAI_Setup_v$Version.exe"
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
        throw "Installer compiler completed but the expected output was not found: $installerPath"
    }
    Invoke-AuthenticodeSign $installerPath
}

$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
$checksumLines = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($releaseDirectory.Length).TrimStart('\', '/').Replace('\', '/')
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Release artifacts created at $releaseDirectory" -ForegroundColor Green
