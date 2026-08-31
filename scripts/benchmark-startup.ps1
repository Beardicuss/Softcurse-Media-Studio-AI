$ErrorActionPreference = "Stop"

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace "artifacts"))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "startup-benchmark"))
$expectedPrefix = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $publishDirectory.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an output path outside the workspace artifacts directory: $publishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$project = Join-Path $workspace "gui\GeminiWatermarkRemover.csproj"
dotnet publish $project -c Release --self-contained false -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "Startup benchmark publish failed with exit code $LASTEXITCODE." }

$executable = Join-Path $publishDirectory "SoftcurseMediaLabAI.exe"
$measurements = @()
for ($run = 1; $run -le 3; $run++) {
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $executable -WorkingDirectory $publishDirectory -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForInputIdle(10000)) {
            throw "The application did not reach an idle UI state within 10 seconds."
        }
        $stopwatch.Stop()
        $process.Refresh()
        $measurements += $stopwatch.Elapsed.TotalMilliseconds
        $privateMb = $process.PrivateMemorySize64 / 1MB
        Write-Host ("Packaged startup run {0}: {1:N0} ms, {2:N1} MB private memory" -f $run, $stopwatch.Elapsed.TotalMilliseconds, $privateMb)
    }
    finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
}

$average = ($measurements | Measure-Object -Average).Average
$budget = if ($average -le 2000) { "budget met" } else { "budget exceeded" }
Write-Host ("Packaged startup average: {0:N0} ms ({1}; target <= 2,000 ms)" -f $average, $budget)
