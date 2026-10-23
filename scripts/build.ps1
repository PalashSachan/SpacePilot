param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Building SpacePilot Solution ($Configuration)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$solutionPath = Join-Path $PSScriptRoot "..\SpacePilot.sln"

Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build $solutionPath --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

Write-Host "Build completed successfully!" -ForegroundColor Green
