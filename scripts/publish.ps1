param (
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "..\publish",
    [switch]$Obfuscate = $false
)

$ErrorActionPreference = "Stop"
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Publishing SpacePilot Standalone Executable" -ForegroundColor Cyan
Write-Host " Target: Windows x64 Self-Contained" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$appProjectPath = Join-Path $PSScriptRoot "..\src\SpacePilot.App\SpacePilot.App.csproj"
$outDir = Join-Path $PSScriptRoot $OutputDirectory

if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

Write-Host "Shutting down any lingering compiler servers..." -ForegroundColor DarkGray
dotnet build-server shutdown | Out-Null

Write-Host "Publishing self-contained Win-x64 binary to $outDir..." -ForegroundColor Yellow
$maxAttempts = 3
$attempt = 1
$published = $false

while (-not $published -and $attempt -le $maxAttempts) {
    try {
        dotnet publish $appProjectPath `
            -c $Configuration `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            -p:UseSharedCompilation=false `
            -o $outDir
            
        if ($LASTEXITCODE -eq 0) {
            $published = $true
        } else {
            throw "Publish exited with code $LASTEXITCODE"
        }
    } catch {
        if ($attempt -lt $maxAttempts) {
            Write-Host "Publish attempt $attempt failed ($($_.Exception.Message)). Waiting 3 seconds to retry..." -ForegroundColor Yellow
            Start-Sleep -Seconds 3
            dotnet build-server shutdown | Out-Null
            $attempt++
        } else {
            throw "dotnet publish failed after $maxAttempts attempts: $($_.Exception.Message)"
        }
    }
}

if ($Obfuscate) {
    Write-Host "Applying Obfuscar assembly protection..." -ForegroundColor Yellow
    $obfuscarConfig = Join-Path $PSScriptRoot "..\obfuscar.xml"
    if (Get-Command "obfuscar.console" -ErrorAction SilentlyContinue) {
        & obfuscar.console $obfuscarConfig
        Write-Host "Obfuscation completed successfully." -ForegroundColor Green
    } else {
        Write-Host "obfuscar.console not found in PATH. Install via: dotnet tool install -g Obfuscar.GlobalTool" -ForegroundColor DarkYellow
    }
}

Write-Host "Publication finished! Output available at:" -ForegroundColor Green
Get-ChildItem -Path $outDir -Filter "*.exe" | ForEach-Object {
    $sizeMb = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.FullName) ($sizeMb MB)" -ForegroundColor Cyan
}
