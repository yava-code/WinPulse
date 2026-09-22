# Pulse for Windows (Native Port)

<p align="center">
  <img src="Pulse.Windows/Resources/app_icon.png" width="96" alt="Pulse Windows">
</p>

<p align="center">
  <b>Native Windows 10 & 11 port of <a href="https://github.com/qunqin24/Pulse">Pulse by qunqin24</a>.</b><br>
  Real-time remaining quotas and rate limits for Claude Code, Codex, Cursor, GitHub Copilot, Antigravity, Grok, DeepSeek, and more.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10.0">
  <img src="https://img.shields.io/badge/UI-WPF%20XAML-0078D6" alt="WPF">
  <img src="https://img.shields.io/badge/Security-DPAPI-green" alt="Windows DPAPI">
  <a href="https://github.com/yava-code/WinPulse/releases/latest"><img src="https://img.shields.io/github/v/release/yava-code/WinPulse?color=blue&label=Latest%20Release" alt="Latest Release"></a>
  <img src="https://img.shields.io/badge/License-Apache%202.0-blue" alt="License">
</p>

---

## 📸 Screenshots

<p align="center">
  <img src="../Docs/images/windows-settings-general.png" width="48%" alt="Pulse Settings - General" />
  &nbsp;
  <img src="../Docs/images/windows-settings-accounts.png" width="48%" alt="Pulse Settings - Accounts & Usage" />
</p>

<p align="center">
  <img src="../Docs/images/windows-floating-rail.png" height="360" alt="Screen-edge floating rail with BotMark mascot rings" />
  <br>
  <em>Screen-Edge Floating Capsule Dock with BotMark Animated Mascot Status Rings</em>
</p>

---

## ⚡ Quick Install (One Command)

You can install Pulse for Windows with a single PowerShell command. This downloads the latest release, installs it to `%LOCALAPPDATA%\Pulse`, creates Start Menu and Desktop shortcuts, and starts the application:

```powershell
irm https://raw.githubusercontent.com/yava-code/WinPulse/main/install.ps1 | iex
```

---

## 📦 Direct Downloads

Pre-built binaries are published with every release on [GitHub Releases](https://github.com/yava-code/WinPulse/releases/latest):

| Asset | Description | Requirements |
| :--- | :--- | :--- |
| **`Pulse.Windows.exe`** | **Standalone Single-File Executable** | **Zero dependencies!** Works directly on any Windows 10/11 (x64 / ARM64). |
| **`Pulse-Windows-x64.zip`** | **Standalone Portable Archive** | Unpack and run anywhere with zero installation. |
| **`Pulse-Windows-x64-framework-dependent.zip`** | Lightweight Package (~2 MB) | Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download). |

---

## 🌟 Key Features

- **Unobtrusive Screen-Edge Rail**: Docks seamlessly to the right, left, or floats freely. Smoothly auto-collapses into an ultra-thin sliver bar against the edge until hovered.
- **BotMark Mascot Kinematics**: 1:1 vector implementation of the BotMark animated mascot face reacting with squash-and-stretch physics to quota exhaustion, active agent turns, and cooldowns.
- **Accurate Quota Progress**: Proportional progress bars ensuring 100% exhaustion accurately fills 100% of the visual track at any screen scaling (100%, 125%, 150%, 200%).
- **Liquid Glass & Obsidian Dark Themes**: Native DirectX layered alpha compositing support for frosted glass without window clipping or rectangular visual artifacts.
- **Windows DPAPI Security**: Credentials, API tokens, and OAuth keys are encrypted using the Windows Data Protection API (`ProtectedData.Protect`), hardware-backed and scoped strictly to your user profile.
- **Multi-Account Discovery**: Dynamic discovery of multiple Antigravity sessions and local config credentials (`%APPDATA%`, `%USERPROFILE%`, Claude Code, Codex, Cursor).
- **System Tray Integration**: Full English notification area menu with live status, quota refresh, and quick settings toggling.
- **Clean Lifecycle**: Kills previous zombie/hung background instances upon start, and forcibly cleans up its own process tree upon exit (`Ctrl+Q`, tray exit, or Settings quit).
- **Zero-GUI CLI Mode**: Run `Pulse.Windows.exe --json` for lightweight scripting with tmux, Komorebi, or custom terminal statusbars.

---

## 🛠️ Build & Development Guide

### Prerequisites
- Windows 10 (Build 1809+) or Windows 11 (x64 / ARM64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### 1-Command Local Build & Test
Use the included automated build script from the repository root:

```powershell
# Build standalone self-contained executable & zip into dist/
.\build.ps1

# Or build lightweight framework-dependent package
.\build.ps1 -FrameworkDependent
```

### Manual dotnet CLI Build
```powershell
# Restore dependencies
dotnet restore Windows/Pulse.slnx

# Run all 34 automated unit tests
dotnet test Windows/Pulse.slnx

# Publish standalone single-file Release binary
dotnet publish Windows/Pulse.Windows/Pulse.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/
```

### Command Line Flags
```powershell
# Dump current cached quota readings as JSON to standard output and exit
.\Pulse.Windows.exe --json

# Launch in console debug mode to inspect real-time provider sync logs
.\Pulse.Windows.exe --debug

# Start directly to system tray and floating rail without opening Settings window
.\Pulse.Windows.exe --minimized
```

---

## 🏷️ Commits & Release Guide

### Commit Message Conventions
Follow Conventional Commits format when creating commits:
- `feat: add new provider support`
- `fix: resolve liquid glass clipping on secondary monitor`
- `docs: update installation instructions and screenshots`
- `refactor: clean up TrayIconManager lifecycle`

### Pushing Changes
To push your commits to the repository:
```powershell
git add .
git commit -m "feat: your descriptive message"
git push winpulse main
```

### Creating an Automated GitHub Release
The repository includes an automated GitHub Actions release workflow (`.github/workflows/release.yml`). To create a release:

1. Create a semantic git tag (e.g. `v1.0.0`):
   ```powershell
   git tag -a v1.0.0 -m "Release v1.0.0"
   ```
2. Push the tag to GitHub:
   ```powershell
   git push winpulse v1.0.0
   ```
3. GitHub Actions will automatically:
   - Restore and build on `windows-latest`
   - Run the automated unit test suite
   - Publish both standalone self-contained and framework-dependent binaries
   - Create a GitHub Release and attach `Pulse.Windows.exe`, `Pulse-Windows-x64.zip`, and `Pulse-Windows-x64-framework-dependent.zip`.

---

## 📜 Attribution & License

- **Port & Windows Implementation**: Forked and maintained in [yava-code/WinPulse](https://github.com/yava-code/WinPulse).
- **Original macOS Application**: Created by [qunqin24](https://github.com/qunqin24) at [qunqin24/Pulse](https://github.com/qunqin24/Pulse).
- **Design Inspiration**: Credited to [Vinz (@hivinz_)](https://x.com/hivinz_).
- **License**: Licensed under [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0).
