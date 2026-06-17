#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes SoundPad as a self-contained single-file win-x64 exe.
.OUTPUTS
    artifacts\publish\SoundPad.App.exe  (plus required WPF native DLLs)
#>

$ErrorActionPreference = "Stop"

$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "SoundPad.App\SoundPad.App.csproj"
$outDir  = Join-Path $root "artifacts\publish"

# Clean old output
Write-Host ""
Write-Host "Cleaning old publish output..." -ForegroundColor Cyan
if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

# Publish
Write-Host "Publishing SoundPad (Release / win-x64 / self-contained / single-file)..." -ForegroundColor Cyan
Write-Host ""

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $outDir

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: dotnet publish failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit 1
}

# Verify exe exists
$exe = Join-Path $outDir "SoundPad.App.exe"
if (-not (Test-Path $exe)) {
    Write-Host ""
    Write-Host "ERROR: SoundPad.App.exe not found in output directory:" -ForegroundColor Red
    Write-Host "  $outDir" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Published successfully." -ForegroundColor Green
Write-Host "  Exe: $exe" -ForegroundColor Yellow
Write-Host "  Dir: $outDir" -ForegroundColor Yellow
