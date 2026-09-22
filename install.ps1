<#
.SYNOPSIS
    Installs Pulse for Windows via one command.
.DESCRIPTION
    Downloads the latest release of Pulse from GitHub, installs it to %LOCALAPPDATA%\Pulse,
    creates Start Menu & Desktop shortcuts, and starts the application.
.EXAMPLE
    irm https://raw.githubusercontent.com/yava-code/WinPulse/main/install.ps1 | iex
#>
[CmdletBinding()]
param(
    [string]$Repo = "yava-code/WinPulse",
    [switch]$NoLaunch = $false,
    [switch]$NoDesktopShortcut = $false
)

$ErrorActionPreference = "Stop"

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "===============================================" -ForegroundColor Cyan
Write-Host "       Pulse for Windows - Installer" -ForegroundColor Cyan
Write-Host "===============================================" -ForegroundColor Cyan

# 1. Kill any existing Pulse instance
$running = Get-Process -Name "Pulse.Windows" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "`nStopping active Pulse process..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 2. Setup install destination
$installDir = Join-Path $env:LOCALAPPDATA "Pulse"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}
$targetExe = Join-Path $installDir "Pulse.Windows.exe"

# 3. Query GitHub Releases for latest release assets
Write-Host "`n[1/3] Checking for latest release from $Repo..." -ForegroundColor Cyan
$downloadUrl = $null
$isZip = $false

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
    $headers = @{ "User-Agent" = "Pulse-Installer" }
    
    $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -ErrorAction Stop
    Write-Host "Found release: $($release.tag_name) ($($release.name))" -ForegroundColor Green

    # Look for assets: prefer standalone zip or exe
    $zipAsset = $release.assets | Where-Object { $_.name -like "*Pulse-Windows-x64.zip" } | Select-Object -First 1
    $exeAsset = $release.assets | Where-Object { $_.name -eq "Pulse.Windows.exe" } | Select-Object -First 1

    if ($zipAsset) {
        $downloadUrl = $zipAsset.browser_download_url
        $isZip = $true
    } elseif ($exeAsset) {
        $downloadUrl = $exeAsset.browser_download_url
        $isZip = $false
    }
} catch {
    Write-Warning "Could not fetch releases via GitHub API ($($_.Exception.Message))."
}

# Fallback direct download URL if API is unauthenticated / rate-limited
if (-not $downloadUrl) {
    $directExeUrl = "https://github.com/$Repo/releases/latest/download/Pulse.Windows.exe"
    $directZipUrl = "https://github.com/$Repo/releases/latest/download/Pulse-Windows-x64.zip"
    Write-Host "Attempting direct download fallback..." -ForegroundColor Yellow
    $downloadUrl = $directZipUrl
    $isZip = $true
}

# 4. Download and extract
Write-Host "`n[2/3] Downloading Pulse package..." -ForegroundColor Cyan
Write-Host "Source: $downloadUrl" -ForegroundColor Gray

$tempPath = Join-Path $env:TEMP ("Pulse_Install_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempPath -Force | Out-Null

try {
    if ($isZip) {
        $tempZip = Join-Path $tempPath "package.zip"
        try {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
        } catch {
            # Try exe directly if zip not found
            $directExeUrl = "https://github.com/$Repo/releases/latest/download/Pulse.Windows.exe"
            Write-Host "Zip not found, trying standalone exe..." -ForegroundColor Yellow
            $tempExe = Join-Path $tempPath "Pulse.Windows.exe"
            Invoke-WebRequest -Uri $directExeUrl -OutFile $tempExe -UseBasicParsing
            $isZip = $false
        }

        if ($isZip) {
            Write-Host "Extracting files to $installDir..." -ForegroundColor Cyan
            Expand-Archive -Path $tempZip -DestinationPath $installDir -Force
        } else {
            Move-Item -Path $tempExe -Destination $targetExe -Force
        }
    } else {
        $tempExe = Join-Path $tempPath "Pulse.Windows.exe"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $tempExe -UseBasicParsing
        Move-Item -Path $tempExe -Destination $targetExe -Force
    }
} catch {
    # Check if local build exists
    $localDistExe = Join-Path $PSScriptRoot "dist\Pulse.Windows.exe"
    if (Test-Path $localDistExe) {
        Write-Host "Using local dist\Pulse.Windows.exe..." -ForegroundColor Yellow
        Copy-Item $localDistExe $targetExe -Force
    } else {
        Write-Error "Failed to download Pulse. Ensure releases are published at https://github.com/$Repo/releases"
        exit 1
    }
} finally {
    if (Test-Path $tempPath) {
        Remove-Item -Path $tempPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $targetExe)) {
    Write-Error "Pulse.Windows.exe was not found in $installDir after extraction."
    exit 1
}

# 5. Create Shortcuts
Write-Host "`n[3/3] Creating shortcuts..." -ForegroundColor Cyan
try {
    $ws = New-Object -ComObject WScript.Shell

    # Start Menu shortcut
    $programsDir = [Environment]::GetFolderPath('Programs')
    $startMenuShortcut = Join-Path $programsDir "Pulse.lnk"
    $shortcut = $ws.CreateShortcut($startMenuShortcut)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = "Pulse - AI Quota & Rate Limit Monitor"
    $shortcut.Save()
    Write-Host "  Start Menu: $startMenuShortcut" -ForegroundColor Gray

    # Desktop shortcut (optional)
    if (-not $NoDesktopShortcut) {
        $desktopDir = [Environment]::GetFolderPath('Desktop')
        $desktopShortcut = Join-Path $desktopDir "Pulse.lnk"
        $dShortcut = $ws.CreateShortcut($desktopShortcut)
        $dShortcut.TargetPath = $targetExe
        $dShortcut.WorkingDirectory = $installDir
        $dShortcut.Description = "Pulse - AI Quota & Rate Limit Monitor"
        $dShortcut.Save()
        Write-Host "  Desktop:    $desktopShortcut" -ForegroundColor Gray
    }
} catch {
    Write-Warning "Could not create shortcuts: $($_.Exception.Message)"
}

Write-Host "`n===============================================" -ForegroundColor Green
Write-Host "       Pulse successfully installed!" -ForegroundColor Green
Write-Host " Location:  $installDir" -ForegroundColor White
Write-Host " Exe:       $targetExe" -ForegroundColor White
Write-Host "===============================================" -ForegroundColor Green

# 6. Launch application
if (-not $NoLaunch) {
    Write-Host "`nLaunching Pulse..." -ForegroundColor Cyan
    Start-Process -FilePath $targetExe -WorkingDirectory $installDir
}
