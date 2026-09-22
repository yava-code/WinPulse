# Pulse for Windows: Porting & Implementation Plan

This document defines the phased implementation strategy for delivering a fully native, high-performance Windows version of Pulse while preserving 100% of existing business logic, models, provider integrations, and macOS stability.

---

## 1. Architectural Philosophy & Strategy

1. **Do Not Emulate macOS**: We do not attempt to force SwiftUI or AppKit onto Windows. Windows receives its own native presentation layer built with C# .NET 10 and WPF with deep Win32 interop.
2. **Preserve Business Logic & UX Principles**:
   - Small floating rail docked to screen edges or floating freely.
   - Non-activating window (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`) so clicking or hovering never steals keyboard focus from user's active editor or terminal.
   - Smooth auto-collapse into a subtle 6pt edge sliver when mouse departs; fluid expansion on hover.
   - Vector usage rings with mathematically accurate progress arcs, status coloring (green/amber/red/spent), and optional second rings.
   - Overlay detail card with reset countdowns, burn rate forecasts, and plan information.
   - Low resource overhead, zero telemetry, no external backend.
3. **Keep macOS Intact**: The macOS build target in `Sources/Pulse/` remains untouched and continues building cleanly on Xcode / Swift 6.

---

## 2. Project Structure

```
d:\pulse-win\Pulse/
├── Sources/Pulse/             # Existing macOS Swift + SwiftUI + AppKit implementation
├── Tests/PulseTests/          # Existing Swift tests & shared JSON fixtures
├── Windows/                   # Native Windows Solution (.NET 10)
│   ├── Pulse.sln
│   ├── Pulse.Core/            # Platform-independent logic, models, parsers, cache
│   │   ├── Models/            # AccountKey, Provider, UsageWindow, ProviderUsage
│   │   ├── Providers/         # Codex, ClaudeCode, Cursor, Copilot, DeepSeek, etc.
│   │   ├── Services/          # UsageStore, AdaptiveRefresh, UsageCache, UsageReport
│   │   ├── Security/          # ICredentialStore, LocalSecrets
│   │   └── Platform/          # IPlatformPaths, IProcessRunner, INotificationService
│   ├── Pulse.Windows/         # Native Windows Presentation & Shell
│   │   ├── Views/             # FloatingRailWindow, DetailCardWindow, SettingsWindow
│   │   ├── Controls/          # UsageRingControl, VectorProgressArc, SliverBar
│   │   ├── SystemIntegration/ # TrayIconManager, WindowNativeMethods, HotkeyService, StartupService
│   │   └── Platform/          # WindowsPlatformPaths, WindowsDPAPICredentialStore
│   └── Pulse.Tests/           # Unit tests validating models & parsers against Fixtures
└── docs/windows/              # Architecture & Porting Documentation
```

---

## 3. Implementation Milestones

### Milestone 0: Architecture Seams & Core Models
- **Goal**: Define domain models, provider protocols, and platform abstractions in C# with identical semantics to Swift.
- **Files**:
  - `Pulse.Core/Models/Provider.cs`
  - `Pulse.Core/Models/UsageWindow.cs`
  - `Pulse.Core/Models/ProviderUsage.cs`
  - `Pulse.Core/Models/AccountKey.cs`
  - `Pulse.Core/Platform/IPlatformPaths.cs`
  - `Pulse.Core/Security/ICredentialStore.cs`
- **Acceptance Criteria**:
  - Core models compile cleanly under .NET 10.
  - JSON serialization parity with existing `--json` contract.
  - Swift macOS codebase untouched.

### Milestone 1: Windows Bootstrap & Floating Rail UI
- **Goal**: Executable Windows application running in system tray with floating always-on-top, non-activating rail window and animated usage rings.
- **Files**:
  - `Pulse.Windows/App.xaml`, `App.xaml.cs`
  - `Pulse.Windows/Views/FloatingRailWindow.xaml`, `FloatingRailWindow.xaml.cs`
  - `Pulse.Windows/Controls/UsageRingControl.xaml.cs`
  - `Pulse.Windows/Controls/DetailCardOverlay.xaml.cs`
  - `Pulse.Windows/SystemIntegration/TrayIconManager.cs`
  - `Pulse.Windows/SystemIntegration/Win32WindowHelper.cs`
- **Deliverables**:
  - Transparent borderless floating window.
  - `WS_EX_TOOLWINDOW` (no taskbar button) and `WS_EX_NOACTIVATE` (no focus stealing).
  - Screen edge docking (left, right, top) and freely draggable.
  - Auto-collapse to 6pt sliver with smooth spring/easing expansion on hover.
  - System tray icon with Context Menu (Settings, Refresh, Quit).
  - Mock usage data pipeline displaying live rings.

### Milestone 2: First Real Providers (Codex, Claude Code, Cursor, DeepSeek)
- **Goal**: Connect live usage data from local files and APIs on Windows.
- **Files**:
  - `Pulse.Core/Providers/CodexUsageService.cs`
  - `Pulse.Core/Providers/ClaudeCodeUsageService.cs`
  - `Pulse.Core/Providers/CursorUsageService.cs`
  - `Pulse.Core/Providers/DeepSeekUsageService.cs`
  - `Pulse.Core/Services/UsageStore.cs`
- **Deliverables**:
  - Codex: auto-discover `%USERPROFILE%\.codex\auth.json` and fetch `wham/usage`.
  - Claude Code: auto-discover `%USERPROFILE%\.claude\.credentials.json` and fetch OAuth usage.
  - Cursor: query `%APPDATA%\Cursor\User\globalStorage\state.vscdb` for JWT token and call `cursor.com/api/usage`.
  - DeepSeek: API key support for balance checking.
  - Live refresh loop with adaptive intervals (2–30 min).

### Milestone 3: Windows Native Integrations & Security
- **Goal**: Hardened Windows services for credentials, autostart, shortcuts, and notifications.
- **Files**:
  - `Pulse.Windows/Platform/WindowsDPAPICredentialStore.cs`
  - `Pulse.Windows/SystemIntegration/WindowsStartupService.cs`
  - `Pulse.Windows/SystemIntegration/WindowsHotkeyService.cs`
  - `Pulse.Windows/SystemIntegration/WindowsNotificationService.cs`
- **Deliverables**:
  - AES-GCM / DPAPI encrypted storage for API keys (`keys.dat`).
  - Windows Run Registry autostart toggle.
  - Global hotkey support (`RegisterHotKey`) to toggle panel visibility.
  - Toast/tray alerts for quota thresholds.

### Milestone 4: Settings UI & Detail Overlay Polish
- **Goal**: Full Settings dialog and rich usage card overlays.
- **Files**:
  - `Pulse.Windows/Views/SettingsWindow.xaml`
  - `Pulse.Windows/Views/DetailCardOverlay.xaml`
- **Deliverables**:
  - Modern dark Fluent settings window.
  - Account selection, provider ordering, API key entry fields.
  - Detail card showing 5h, weekly, and model-specific windows, reset timers, and spend forecasts.

### Milestone 5: CI & Distribution
- **Goal**: Windows automated builds and tests on GitHub Actions.
- **Files**:
  - `.github/workflows/ci.yml` (Windows matrix addition)
- **Deliverables**:
  - `.github/workflows/ci.yml` builds both macOS and Windows.
  - Automated tests running on `windows-latest`.
  - Single-file portable self-contained build artifact.
