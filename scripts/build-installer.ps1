#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes SoundPad and compiles the Inno Setup installer.
.REQUIREMENTS
    Inno Setup 6 or 7 must be installed.
    Download from: https://jrsoftware.org/isinfo.php
#>

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

# Step 1 - Publish
Write-Host ""
Write-Host "Step 1 / 2 - Publishing application..." -ForegroundColor Cyan
& "$PSScriptRoot\publish-release.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Publish step failed. See errors above." -ForegroundColor Red
    exit 1
}

# Step 2 - Find Inno Setup
Write-Host ""
Write-Host "Step 2 / 2 - Building installer..." -ForegroundColor Cyan

$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
foreach ($candidate in $isccCandidates) {
    if (Test-Path $candidate) {
        $isccPath = $candidate
        break
    }
}

if (-not $isccPath) {
    Write-Host ""
    Write-Host "ERROR: Inno Setup compiler (ISCC.exe) was not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "Install Inno Setup from:  https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Searched these paths:" -ForegroundColor Gray
    foreach ($candidate in $isccCandidates) {
        Write-Host "  $candidate" -ForegroundColor Gray
    }
    exit 1
}

Write-Host "  Found: $isccPath" -ForegroundColor Gray

# Compile installer
$issPath      = Join-Path $root "installer\SoundPad.iss"
$outInstaller = Join-Path $root "artifacts\installer\SoundPad-Setup-1.9.0.exe"

Write-Host "  Script: $issPath" -ForegroundColor Gray
Write-Host ""

& $isccPath $issPath

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Inno Setup compilation failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Installer built successfully." -ForegroundColor Green
Write-Host "  $outInstaller" -ForegroundColor Yellow
