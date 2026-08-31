$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace "tests\SoftcurseMediaLabAI.Benchmarks\SoftcurseMediaLabAI.Benchmarks.csproj"
$model = Join-Path $workspace "gui\models\lama_fp32.onnx"
dotnet run --project $project -c Release -- $model
if ($LASTEXITCODE -ne 0) { throw "Benchmark run failed with exit code $LASTEXITCODE." }
