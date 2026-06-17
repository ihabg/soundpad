<#
.SYNOPSIS
    Publishes SoundPad and compiles the Inno Setup installer.

.DESCRIPTION
    1. Runs publish-release.ps1 to produce a clean win-x64 publish output.
    2. Locates ISCC.exe (Inno Setup 6 or 7).
    3. Compiles installer/SoundPad.iss and places the result in
       artifacts/installer/SoundPad-Setup-1.0.0.exe.

.REQUIREMENTS
    Inno Setup 6 or 7 — https://jrsoftware.org/isinfo.php
#>

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

# ── Step 1: Publish ───────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Step 1 / 2 — Publishing application..." -ForegroundColor Cyan
& "$PSScriptRoot\publish-release.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Publish step failed. See errors above." -ForegroundColor Red
    exit 1
}

# ── Step 2: Locate Inno Setup ─────────────────────────────────────────────────
Write-Host ""
Write-Host "Step 2 / 2 — Building installer..." -ForegroundColor Cyan

$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "ERROR: Inno Setup compiler (ISCC.exe) not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "Install Inno Setup from:  https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Searched:" -ForegroundColor Gray
    $isccCandidates | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    exit 1
}

Write-Host "  Found: $iscc" -ForegroundColor Gray

# ── Compile ───────────────────────────────────────────────────────────────────
$issFile      = Join-Path $root "installer\SoundPad.iss"
$outInstaller = Join-Path $root "artifacts\installer\SoundPad-Setup-1.0.0.exe"

Write-Host "  Script: $issFile" -ForegroundColor Gray
Write-Host ""

& $iscc $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Inno Setup compilation failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit 1
}

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Installer built successfully." -ForegroundColor Green
Write-Host "  $outInstaller" -ForegroundColor Yellow
