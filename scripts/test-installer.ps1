param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [string]$PreviousInstallerPath,
    [switch]$RequireModel
)

$ErrorActionPreference = "Stop"
$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace "artifacts"))
$smokeRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "installer-smoke"))
$installDirectory = [System.IO.Path]::GetFullPath((Join-Path $smokeRoot "installed"))
$allowedPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $smokeRoot.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use an installer-test path outside workspace artifacts: $smokeRoot"
}
if (Test-Path -LiteralPath $smokeRoot) {
    Remove-Item -LiteralPath $smokeRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null

$installer = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer not found: $installer"
}

function Invoke-Installer([string]$Path, [string]$LogName) {
    $arguments = @(
        "/CURRENTUSER",
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/NOCANCEL",
        "/DIR=`"$installDirectory`"",
        "/LOG=`"$(Join-Path $smokeRoot $LogName)`""
    )
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    try {
        if ($process.ExitCode -ne 0) { throw "Installer exited with code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}

if (-not [string]::IsNullOrWhiteSpace($PreviousInstallerPath)) {
    $previous = [System.IO.Path]::GetFullPath($PreviousInstallerPath)
    if (-not (Test-Path -LiteralPath $previous -PathType Leaf)) {
        throw "Previous installer not found: $previous"
    }
    Invoke-Installer $previous "install-previous.log"
    if (-not (Test-Path -LiteralPath (Join-Path $installDirectory "SoftcurseMediaLabAI.exe"))) {
        throw "Previous-version installation did not create the application executable."
    }
    Invoke-Installer $installer "upgrade-current.log"
} else {
    Invoke-Installer $installer "install-current.log"
}

$app = Join-Path $installDirectory "SoftcurseMediaLabAI.exe"
$uninstaller = Join-Path $installDirectory "unins000.exe"
$ffmpeg = Join-Path $installDirectory "tools\ffmpeg\ffmpeg.exe"
$ffprobe = Join-Path $installDirectory "tools\ffmpeg\ffprobe.exe"
$ffmpegLicense = Join-Path $installDirectory "tools\ffmpeg\LICENSE.txt"
$ffmpegSource = Join-Path $installDirectory "tools\ffmpeg\SOURCE.txt"
$runtime = Join-Path $installDirectory "coreclr.dll"
foreach ($required in @($app, $uninstaller, $ffmpeg, $ffprobe, $ffmpegLicense, $ffmpegSource, $runtime)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Installed file is missing: $required"
    }
}

$ffmpegVersion = & $ffmpeg -version 2>&1 | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or $ffmpegVersion -notmatch '^ffmpeg version ') {
    throw "Installed bundled FFmpeg failed its version check: $ffmpegVersion"
}
$model = Join-Path $installDirectory "models\lama_fp32.onnx"
if ($RequireModel -and -not (Test-Path -LiteralPath $model -PathType Leaf)) {
    throw "Installed LaMa model is missing: $model"
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion
if ($fileVersion -ne "$ExpectedVersion.0") {
    throw "Installed version mismatch. Expected $ExpectedVersion.0, found $fileVersion."
}

$appProcess = Start-Process -FilePath $app -WorkingDirectory $installDirectory -WindowStyle Hidden -PassThru
try {
    if (-not $appProcess.WaitForInputIdle(10000)) {
        throw "Installed application did not reach an idle UI state within 10 seconds."
    }
    if ($appProcess.HasExited) {
        throw "Installed application exited unexpectedly with code $($appProcess.ExitCode)."
    }
}
finally {
    if (-not $appProcess.HasExited) {
        $null = $appProcess.CloseMainWindow()
        if (-not $appProcess.WaitForExit(3000)) { Stop-Process -Id $appProcess.Id -Force }
    }
    $appProcess.Dispose()
}

$uninstallArguments = @(
    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
    "/LOG=`"$(Join-Path $smokeRoot 'uninstall.log')`""
)
$uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArguments -WindowStyle Hidden -Wait -PassThru
try {
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstallProcess.ExitCode)."
    }
}
finally { $uninstallProcess.Dispose() }

if (Test-Path -LiteralPath $app) {
    throw "Uninstall left the application executable behind: $app"
}

$successMessage = if ($PreviousInstallerPath) {
    "Installer upgrade/launch/uninstall test passed."
} else {
    "Installer clean-install/launch/uninstall test passed."
}
Write-Host $successMessage -ForegroundColor Green
