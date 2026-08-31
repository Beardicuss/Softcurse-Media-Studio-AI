param(
    [string]$DestinationDirectory
)

$ErrorActionPreference = "Stop"
$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace "artifacts"))
$cacheDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "cache\ffmpeg-9.0.1"))
$allowedPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $cacheDirectory.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use an FFmpeg cache outside workspace artifacts: $cacheDirectory"
}

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $workspace "third_party\ffmpeg"
}
$destination = [System.IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null

$required = @("ffmpeg.exe", "ffprobe.exe", "LICENSE.txt", "SOURCE.txt", "BUILD-README.txt")
if (-not ($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf) })) {
    Write-Host "Pinned FFmpeg payload is already available at $destination" -ForegroundColor Green
    exit 0
}

New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
$archive = Join-Path $cacheDirectory "ffmpeg-9.0.1-essentials_build.7z"
$archiveUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.1-essentials_build.7z"
$expectedSha256 = "49a73bdf0850092a252ac4641d922f3048d63ed113e196cc65ce1e4f7fb33e85"

if (-not (Test-Path -LiteralPath $archive -PathType Leaf) -or
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedSha256) {
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archive -UseBasicParsing
}
$actualSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256) {
    throw "FFmpeg archive checksum mismatch. Expected $expectedSha256, found $actualSha256."
}

$extractDirectory = Join-Path $cacheDirectory "extracted"
if (Test-Path -LiteralPath $extractDirectory) {
    Remove-Item -LiteralPath $extractDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $extractDirectory -Force | Out-Null
& tar.exe -xf $archive -C $extractDirectory
if ($LASTEXITCODE -ne 0) { throw "FFmpeg archive extraction failed with exit code $LASTEXITCODE." }

$sourceRoot = Get-ChildItem -LiteralPath $extractDirectory -Directory | Select-Object -First 1
if (-not $sourceRoot) { throw "The FFmpeg archive did not contain its expected root directory." }
Copy-Item -LiteralPath (Join-Path $sourceRoot.FullName "bin\ffmpeg.exe") -Destination (Join-Path $destination "ffmpeg.exe") -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot.FullName "bin\ffprobe.exe") -Destination (Join-Path $destination "ffprobe.exe") -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot.FullName "LICENSE") -Destination (Join-Path $destination "LICENSE.txt") -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot.FullName "README.txt") -Destination (Join-Path $destination "BUILD-README.txt") -Force

foreach ($fileName in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $destination $fileName) -PathType Leaf)) {
        throw "Prepared FFmpeg payload is missing $fileName."
    }
}
Write-Host "Prepared checksum-verified FFmpeg 9.0.1 payload at $destination" -ForegroundColor Green
