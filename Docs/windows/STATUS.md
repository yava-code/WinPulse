# Current Status: Pulse for Windows

## Status Overview
- **Phase**: Milestone 0 & Milestone 1 Completed; Milestone 2 in Progress
- **Last Updated**: 2026-09-22
- **Build Status**: Passing (0 warnings, 0 errors, 20/20 tests passed)

## Working
- **Full Architecture Audit & ADR-001**: .NET 10 + WPF with Win32 Interop chosen and documented.
- **Provider Compatibility Matrix**: All 20 quota providers and 54 token spend sources audited.
- **Core Models (`Pulse.Core`)**:
  - `Provider`, `AccountKey`, `UsageWindow`, `ProviderUsage`, `UsageTint`.
  - Exact Pulse display rules (no 0% if used, no 100% until full, `AwayFromZero` rounding).
  - Multi-window and headline/fullest window selection.
- **Real Providers**:
  - **Codex**: Reads `%USERPROFILE%\.codex\auth.json` and fetches `wham/usage`.
  - **Claude Code**: Reads `%USERPROFILE%\.claude\.credentials.json` and fetches `api.anthropic.com/api/oauth/usage`.
  - **Cursor**: Reads `%APPDATA%\Cursor\User\globalStorage\state.vscdb` (SQLite read-only), decodes JWT payload, formats session cookie, and queries `cursor.com/api/usage`.
  - **DeepSeek**: Balance querying via API key in DPAPI store.
- **Storage & Security**:
  - `WindowsDPAPICredentialStore`: AES / DPAPI (`CryptProtectData`) encrypted storage in `%LOCALAPPDATA%\Pulse\keys.dat`.
  - `UsageCache`: Rapid disk caching in `%LOCALAPPDATA%\Pulse\cache.json`.
  - `UsageReport`: Produces JSON output strictly adhering to macOS `Pulse --json` contract.
- **Native Windows Presentation (`Pulse.Windows`)**:
  - `FloatingRailWindow`: Transparent, borderless, non-activating (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`). Never steals focus from active IDEs.
  - Smooth auto-collapse into 6px edge sliver (`SliverBar`) with cubic ease expansion on hover.
  - Alert tinting on sliver bar (green/amber/red/spent) when limits approach threshold.
  - Screen edge docking (left/right) with drag and snap.
  - `UsageRingControl`: Smooth animated vector progress arcs, glowing hover halo, and central brand badges.
  - `DetailCardPopup`: Rich overlay card with reset countdowns, progress bars, and credit balances.
  - `TrayIconManager`: System tray icon with context menu (Settings, Refresh Now, Show/Hide Rail, Quit).
  - `SettingsWindow`: Dark mode dialog for toggling providers, managing API keys, and startup options.
- **Automated Tests (`Pulse.Tests`)**:
  - 20 unit tests covering rounding, thresholds, reset formatting, Codex response parsing, plan mapping, and JSON schema parity. All passing.
- **CI / CD**:
  - `.github/workflows/ci.yml` updated with Windows .NET 10 build & test matrix job.

## In Progress / Next Implementation Steps
- Antigravity process and TCP listening port discovery (`language_server_windows_x64.exe`).
- Windows Global Hotkey (`RegisterHotKey`) for toggling rail visibility.
- Windows Startup registration (Registry Run key).
- Chromium DPAPI cookie decryption for Ollama Cloud and Xiaomi Coding Plan.
- Windows Toast Notifications dispatch.
