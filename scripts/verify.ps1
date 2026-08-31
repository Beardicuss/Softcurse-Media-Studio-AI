$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $workspace "gui\GeminiWatermarkRemover.csproj"
$testProject = Join-Path $workspace "tests\SoftcurseMediaLabAI.Tests\SoftcurseMediaLabAI.Tests.csproj"

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

Invoke-Checked { dotnet restore $appProject }
Invoke-Checked { dotnet restore $testProject }
Invoke-Checked { dotnet build $appProject -c Release --no-restore }
Invoke-Checked { dotnet test $testProject -c Release --no-restore }
Invoke-Checked { dotnet list $appProject package --vulnerable --include-transitive }

Push-Location $workspace
try {
    Invoke-Checked { git diff --check }
    $leftovers = rg -n "SpriteGenerator|SpriteSheetService|SdWebUiManager" gui -g "!bin" -g "!obj"
    if ($LASTEXITCODE -eq 0) {
        throw "Removed sprite-generator references were found:`n$leftovers"
    }
    if ($LASTEXITCODE -ne 1) {
        throw "Repository search failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

Write-Host "Softcurse verification passed." -ForegroundColor Green
