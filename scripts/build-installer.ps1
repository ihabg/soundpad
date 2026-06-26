#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes SoundPad, compiles the Inno Setup installer, and produces all
    release artifacts: installer, portable ZIP, SHA256 files, and a manifest.
.REQUIREMENTS
    Inno Setup 6 or 7 must be installed.
    Download from: https://jrsoftware.org/isinfo.php
.OUTPUTS
    artifacts\installer\SoundPad-Setup-X.Y.Z.exe
    artifacts\installer\SoundPad-Setup-X.Y.Z.exe.sha256
    artifacts\installer\SoundPad-Portable-X.Y.Z.zip
    artifacts\installer\SoundPad-Portable-X.Y.Z.zip.sha256
    artifacts\installer\release-manifest.json
#>

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

# -- Read version from installer/SoundPad.iss ---------------------------------
$issPath = Join-Path $root "installer\SoundPad.iss"
if (-not (Test-Path $issPath)) {
    Write-Host "[ERROR] installer\SoundPad.iss not found." -ForegroundColor Red
    exit 1
}

$issContent = Get-Content $issPath -Raw
if ($issContent -notmatch '#define AppVersion\s+"([^"]+)"') {
    Write-Host "[ERROR] Could not read AppVersion from SoundPad.iss." -ForegroundColor Red
    exit 1
}
$appVersion = $Matches[1]
Write-Host ""
Write-Host "[INFO] Version: $appVersion" -ForegroundColor Cyan

# -- Step 1 / 3 - Publish -----------------------------------------------------
Write-Host ""
Write-Host "Step 1 / 3 - Publishing application..." -ForegroundColor Cyan
& "$PSScriptRoot\publish-release.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Publish step failed. See errors above." -ForegroundColor Red
    exit 1
}

$publishDir = Join-Path $root "artifacts\publish"
if (-not (Test-Path (Join-Path $publishDir "SoundPad.App.exe"))) {
    Write-Host ""
    Write-Host "[ERROR] artifacts\publish\SoundPad.App.exe not found after publish." -ForegroundColor Red
    exit 1
}

# -- Step 2 / 3 - Build installer ---------------------------------------------
Write-Host ""
Write-Host "Step 2 / 3 - Building installer..." -ForegroundColor Cyan

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
    Write-Host "[ERROR] Inno Setup compiler (ISCC.exe) was not found." -ForegroundColor Red
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
Write-Host ""

& $isccPath $issPath

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Inno Setup compilation failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit 1
}

$installerName = "SoundPad-Setup-$appVersion.exe"
$installerPath = Join-Path $root "artifacts\installer\$installerName"

if (-not (Test-Path $installerPath)) {
    Write-Host ""
    Write-Host "[ERROR] Installer was not created at expected path:" -ForegroundColor Red
    Write-Host "  $installerPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "ISCC may have output to a different location. Check the log above." -ForegroundColor Yellow
    exit 1
}

Write-Host "[OK] Installer created: $installerName" -ForegroundColor Green

# -- Step 3 / 3 - Generate release artifacts ----------------------------------
Write-Host ""
Write-Host "Step 3 / 3 - Generating release artifacts..." -ForegroundColor Cyan

$outDir = Join-Path $root "artifacts\installer"

# SHA256 for installer
$installerHash     = (Get-FileHash $installerPath -Algorithm SHA256).Hash
$installerSha256File = Join-Path $outDir "$installerName.sha256"
"$installerHash  $installerName" | Set-Content $installerSha256File -Encoding UTF8
Write-Host "  [OK] Installer checksum written." -ForegroundColor Gray

# Portable ZIP
$portableName = "SoundPad-Portable-$appVersion.zip"
$portablePath = Join-Path $outDir $portableName

if (Test-Path $portablePath) {
    Remove-Item $portablePath -Force
}

Write-Host "  Creating portable ZIP..." -ForegroundColor Gray
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portablePath -CompressionLevel Optimal
Write-Host "  [OK] Portable ZIP created." -ForegroundColor Gray

# SHA256 for portable ZIP
$portableHash      = (Get-FileHash $portablePath -Algorithm SHA256).Hash
$portableSha256File = Join-Path $outDir "$portableName.sha256"
"$portableHash  $portableName" | Set-Content $portableSha256File -Encoding UTF8
Write-Host "  [OK] Portable ZIP checksum written." -ForegroundColor Gray

# Release manifest
$releaseDate  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$manifestPath = Join-Path $outDir "release-manifest.json"

$manifest = [ordered]@{
    appName     = "SoundPad"
    version     = $appVersion
    releaseDate = $releaseDate
    installer   = [ordered]@{
        filename = $installerName
        sha256   = $installerHash
    }
    portable    = [ordered]@{
        filename = $portableName
        sha256   = $portableHash
    }
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content $manifestPath -Encoding UTF8
Write-Host "  [OK] Release manifest written." -ForegroundColor Gray

# -- Summary ------------------------------------------------------------------
function Format-Bytes([long]$bytes) {
    if ($bytes -ge 1MB) { return "{0:N1} MB" -f ($bytes / 1MB) }
    return "{0:N0} KB" -f ($bytes / 1KB)
}

$divider = "================================================================"

Write-Host ""
Write-Host $divider -ForegroundColor Green
Write-Host "  SoundPad v$appVersion - Release Artifacts" -ForegroundColor Green
Write-Host $divider -ForegroundColor Green
Write-Host ""

$artifacts = @(
    [PSCustomObject]@{ File = $installerName;            Path = $installerPath       }
    [PSCustomObject]@{ File = "$installerName.sha256";   Path = $installerSha256File }
    [PSCustomObject]@{ File = $portableName;             Path = $portablePath        }
    [PSCustomObject]@{ File = "$portableName.sha256";    Path = $portableSha256File  }
    [PSCustomObject]@{ File = "release-manifest.json";  Path = $manifestPath        }
)

foreach ($a in $artifacts) {
    $size = (Get-Item $a.Path).Length
    Write-Host ("  {0,-50} {1,8}" -f $a.File, (Format-Bytes $size)) -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  Installer SHA256 : $installerHash" -ForegroundColor Cyan
Write-Host "  Portable  SHA256 : $portableHash"  -ForegroundColor Cyan
Write-Host ""
Write-Host "  All artifacts in: artifacts\installer\" -ForegroundColor Gray
Write-Host ""
Write-Host "  NOTE: Verify the artifacts\ folder contains only intended files" -ForegroundColor Gray
Write-Host "        before uploading to GitHub Releases." -ForegroundColor Gray
Write-Host ""
Write-Host $divider -ForegroundColor Green
Write-Host "  [OK] Build complete." -ForegroundColor Green
Write-Host $divider -ForegroundColor Green
Write-Host ""
