# Known Gaps & Platform Differences: Pulse for Windows

This document details known behavioral and platform-specific gaps between the macOS and Windows implementations of Pulse, and how they are addressed.

---

## 1. Application Lifecycle & Shell

| Feature | macOS Behavior | Windows Equivalent / Gap Strategy |
|---|---|---|
| **Activation Policy** | `NSApplication.ActivationPolicy.accessory` (no Dock icon) | Tool Window style (`WS_EX_TOOLWINDOW`) combined with `ShowInTaskbar = false`. Windows prevents taskbar presence entirely. |
| **Focus Stealing** | Non-activating `NSPanel` (`canBecomeKey = false`, `canBecomeMain = false`) | `WS_EX_NOACTIVATE` extended window style + intercepting `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`. Clicking, dragging, or hovering on Pulse never deactivates the user's active editor or console. |
| **Menu Bar / System Tray** | `MenuBarExtra` in macOS Menu Bar | Windows System Tray (`NotifyIcon`) with dark Fluent context menu. |
| **Login Item** | `SMAppService.mainApp` or `~/Library/LaunchAgents` | Windows Registry Run key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) or Startup Folder shortcut. |
| **Updates** | Sparkle framework (`SPUStandardUpdaterController`) | Sparkle is macOS-only. On Windows, `IUpdateService` provides non-blocking GitHub Release check or acts as a clean no-op stub for portable execution. |

---

## 2. Windowing, Geometry & Screen Edge Docking

| Feature | macOS Behavior | Windows Equivalent / Gap Strategy |
|---|---|---|
| **Screen Edge Docking** | Docks to Left, Right, or Top edge; auto-collapses to 6pt sliver | Fully supported. Calculates screen bounds via `System.Windows.Forms.Screen.FromPoint` or Win32 `GetMonitorInfo`. Snaps within `dockDistance` (24px). |
| **Hardware Notch Housing** | Attaches beneath Apple MacBook screen notch via `auxiliaryTopLeftArea` / `safeAreaInsets.top` | Not applicable on Windows. Top-docking attaches cleanly to the upper screen edge with symmetric curvature. |
| **Per-Monitor DPI Scaling** | Handled transparently by macOS AppKit / CoreGraphics | Handled via Windows `PerMonitorV2` DPI awareness. Coordinates and sizes are scaled accurately across displays with differing scale factors (e.g. 100%, 150%, 200%). |
| **Active Display Follower** | 0.25s timer sampling pointer position across `NSScreen.screens` | `System.Windows.Forms.Cursor.Position` mapped to active `Screen`. Smoothly relocates the rail when pointer transitions monitors (when enabled in Settings). |

---

## 3. Cryptography & Credentials

| Feature | macOS Behavior | Windows Equivalent / Gap Strategy |
|---|---|---|
| **API Keys Storage** | `keys.dat` encrypted with AES-GCM; key derived from `kIOPlatformUUIDKey` + purpose via SHA-256 | Windows DPAPI (`ProtectedData.Protect` / `Unprotect` with `DataProtectionScope.CurrentUser` and purpose as entropy). No raw keys stored in plaintext. |
| **CLI Credentials Discovery** | Claude Code via `/usr/bin/security find-generic-password` | Claude Code on Windows stores credentials in `%USERPROFILE%\.claude\.credentials.json` or Windows Credential Manager. |
| **Browser Cookie Decryption** | Chromium cookies decrypted using key from macOS Keychain (`Chrome Safe Storage`) via PBKDF2/AES-CBC | On Windows, Chromium (Chrome, Edge, Brave) stores an encrypted master key in `%LOCALAPPDATA%\...\User Data\Local State`. Decrypted via DPAPI, then used with AES-256-GCM to decrypt cookie values. |

---

## 4. Providers Status for Windows

| Provider | Windows Support Level | Notes |
|---|---|---|
| **Codex** | Full | Reads `%USERPROFILE%\.codex\auth.json`, HTTPS direct fetch. |
| **Claude Code** | Full | Reads `%USERPROFILE%\.claude\.credentials.json` or Windows Credential Manager. |
| **Cursor** | Full | Reads `%APPDATA%\Cursor\User\globalStorage\state.vscdb` (SQLite read-only). |
| **GitHub Copilot** | Full | GitHub device OAuth code grant; token stored in DPAPI `keys.dat`. |
| **API Key Providers** | Full | DeepSeek, Kimi Code, z.ai, OpenCode Go, MiniMax, Command Code. |
| **Antigravity** | In Progress | Windows process inspection (`language_server_windows_x64.exe`) and loopback port discovery. |
| **Devin** | Partial | Pasted token fully supported; Chromium LevelDB reader scheduled for Milestone 3. |
| **Ollama Cloud** | Partial | Firefox cookie reader and manual session supported; Chromium DPAPI cookie decryption scheduled for Milestone 3. |
| **Kiro** | Partial | Windows CLI process runner via ACP stream scheduled for Milestone 3. |
