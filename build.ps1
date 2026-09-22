<#
.SYNOPSIS
    Builds, tests, and packages Pulse for Windows.
.DESCRIPTION
    Runs unit tests, restores dependencies, and publishes single-file standalone or framework-dependent binaries into the dist/ directory.
.PARAMETER FrameworkDependent
    If specified, builds a lightweight ~2MB executable requiring .NET 10 Desktop Runtime installed.
    Otherwise builds a standalone self-contained executable (~160MB) that runs everywhere with zero dependencies.
.PARAMETER Configuration
    Build configuration (default: Release).
#>
[CmdletBinding()]
param(
    [switch]$FrameworkDependent = $false,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=======================================" -ForegroundColor Cyan
Write-Host " 🚀 Building Pulse for Windows" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan

# 1. Run unit tests
Write-Host "`n[1/3] Running automated unit tests..." -ForegroundColor Yellow
dotnet test Windows/Pulse.slnx --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed!"
    exit $LASTEXITCODE
}

# 2. Publish
$selfContained = -not $FrameworkDependent
$outputDir = Join-Path $PSScriptRoot "dist"
if (Test-Path $outputDir) {
    try {
        Remove-Item $outputDir -Recurse -Force -ErrorAction SilentlyContinue
    } catch { }
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "`n[2/3] Publishing Pulse.Windows (Self-Contained: $selfContained)..." -ForegroundColor Yellow
if ($selfContained) {
    dotnet publish "$PSScriptRoot/Windows/Pulse.Windows/Pulse.Windows.csproj" `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $outputDir
} else {
    dotnet publish "$PSScriptRoot/Windows/Pulse.Windows/Pulse.Windows.csproj" `
        -c $Configuration `
        -r win-x64 `
        --self-contained false `
        -p:PublishSingleFile=true `
        -o $outputDir
}

# 3. Create zip package
Write-Host "`n[3/3] Packaging distribution zip..." -ForegroundColor Yellow
$zipPath = Join-Path $outputDir "Pulse-Windows-x64.zip"
$filesToZip = @(Join-Path $outputDir "Pulse.Windows.exe")
$resDir = Join-Path $outputDir "Resources"
if (Test-Path $resDir) {
    $filesToZip += $resDir
}
Compress-Archive -Path $filesToZip -DestinationPath $zipPath -Force

$exeItem = Get-Item (Join-Path $outputDir "Pulse.Windows.exe")
$exeSizeMB = [math]::Round($exeItem.Length / 1MB, 2)
$zipSizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Write-Host "`n=======================================" -ForegroundColor Green
Write-Host " ✅ Build successful!" -ForegroundColor Green
Write-Host " Standalone Executable: $(Join-Path $outputDir 'Pulse.Windows.exe') ($exeSizeMB MB)" -ForegroundColor White
Write-Host " Zip Archive:          $zipPath ($zipSizeMB MB)" -ForegroundColor White
Write-Host "=======================================" -ForegroundColor Green
