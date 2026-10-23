param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Running SpacePilot Automated Test Suite" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$testProjectPath = Join-Path $PSScriptRoot "..\tests\SpacePilot.UnitTests\SpacePilot.UnitTests.csproj"

dotnet test $testProjectPath --configuration $Configuration --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

Write-Host "All automated tests executed successfully!" -ForegroundColor Green
