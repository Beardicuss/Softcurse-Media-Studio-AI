param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$RequireModel,
    [switch]$RequireFfmpeg,
    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"
$publish = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publish -PathType Container)) {
    throw "Publish directory does not exist: $publish"
}

$requiredFiles = @(
    "SoftcurseMediaLabAI.exe",
    "SoftcurseMediaLabAI.dll",
    "SoftcurseMediaLabAI.deps.json",
    "SoftcurseMediaLabAI.runtimeconfig.json",
    "coreclr.dll",
    "hostfxr.dll",
    "hostpolicy.dll"
)
foreach ($relativePath in $requiredFiles) {
    $candidate = Join-Path $publish $relativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Required package file is missing: $relativePath"
    }
}

$forbidden = Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
    $_.Extension -in @(".pdb", ".obj", ".lib") -or
    $_.Name -in @("settings.json", "appsettings.Development.json")
}
if ($forbidden) {
    throw "Development or user-specific files were found in the package: $($forbidden.FullName -join ', ')"
}

$model = Join-Path $publish "models\lama_fp32.onnx"
if ($RequireModel -and -not (Test-Path -LiteralPath $model -PathType Leaf)) {
    throw "The release requires models\lama_fp32.onnx, but it is missing."
}

if ($RequireFfmpeg) {
    foreach ($relativePath in @(
        "tools\ffmpeg\ffmpeg.exe",
        "tools\ffmpeg\ffprobe.exe",
        "tools\ffmpeg\LICENSE.txt",
        "tools\ffmpeg\SOURCE.txt",
        "tools\ffmpeg\BUILD-README.txt"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $relativePath) -PathType Leaf)) {
            throw "The self-contained release requires $relativePath, but it is missing."
        }
    }
}

$exe = Join-Path $publish "SoftcurseMediaLabAI.exe"
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$expectedFileVersion = "$Version.0"
if ($versionInfo.FileVersion -ne $expectedFileVersion) {
    throw "Executable version mismatch. Expected $expectedFileVersion, found $($versionInfo.FileVersion)."
}

if ($RequireSignature) {
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Application signature is not valid: $($signature.Status) $($signature.StatusMessage)"
    }
}

$totalBytes = (Get-ChildItem -LiteralPath $publish -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Package validation passed: {0} files, {1:N1} MB, version {2}" -f `
    (Get-ChildItem -LiteralPath $publish -Recurse -File).Count, ($totalBytes / 1MB), $Version) -ForegroundColor Green
if (-not (Test-Path -LiteralPath $model)) {
    Write-Warning "LaMa model is not included. This package is suitable for CI shell validation, not a full AI release."
}
