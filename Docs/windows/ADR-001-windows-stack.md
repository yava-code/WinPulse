# ADR-001: Windows Technology Stack Selection

- **Status**: Accepted
- **Date**: 2026-09-21
- **Deciders**: Architecture Team
- **Context**: Porting Pulse (macOS menu bar & floating rail usage monitor) to Windows 10/11.

---

## 1. Context and Problem Statement

Pulse is a real-time AI quota and token-spend monitor. On macOS, it is built with Swift, SwiftUI, and AppKit (`NSPanel`). Its defining UX characteristics are:
- A transparent, borderless, floating rail window docked to screen edges (left, right, or top) or floating freely.
- Always-on-top level (`.floating` / `.statusBar`).
- Non-activating window behavior (`canBecomeKey = false`, `canBecomeMain = false`) so clicking or hovering on rings never steals input focus from active IDEs or terminals.
- Fluid auto-collapse to a 6pt sliver at screen edges, expanding with spring animations on mouse hover.
- Dynamic detail cards appearing as overlays with animated pointer shapes.
- System tray integration (settings, manual refresh, quit).
- Multi-monitor tracking (optional pointer display following).
- Secure local credential storage (no plaintext tokens).
- High performance, low idle resource consumption, zero distraction.

We need to choose the technology stack for Pulse on Windows that maximizes fidelity to these UX requirements, maintains developer velocity, ensures rock-solid stability, and integrates natively with Windows APIs.

---

## 2. Decision Drivers

1. **Windowing & Composition**: Must support transparent, borderless, always-on-top, tool-window (`WS_EX_TOOLWINDOW`), non-activating (`WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`), click-through on transparent areas, and smooth hardware-accelerated vector drawing.
2. **Animation & Visual Polish**: Custom geometry (superellipse corners, bezier flares, circular rings, animated progress arcs, glowing halos, fluid hover/expand).
3. **System Integration**: System tray (NotifyIcon), global hotkeys (`RegisterHotKey`), multi-monitor DPI awareness (PerMonitorV2), autostart (Registry/Task), Windows notifications (Toast/Balloon).
4. **Security & Cryptography**: Native Windows DPAPI (`CryptProtectData` / `ProtectedData`) and Windows Credential Manager (`CredWriteW` / `CredReadW`) for encrypting API keys and OAuth tokens.
5. **Data & Storage Access**: SQLite reader (read-only WAL mode for `state.vscdb`, browser cookies, etc.), Chromium `Local State` DPAPI master key decryption, JSON streaming.
6. **Toolchain & Developer Environment**: Host environment has .NET 10 SDK (`10.0.201`) and `Microsoft.WindowsDesktop.App` (WPF) pre-installed. Swift compiler is not available on Windows without complex unofficial toolchains.
7. **Maintainability**: Clear separation between core logic (models, providers, parsers, cache) and platform shell.

---

## 3. Options Considered

### Option A: C# / .NET 10 + WPF (Chosen)

- **Pros**:
  - **Perfect Match for Floating Overlay**: WPF has native `WindowStyle="None" AllowsTransparency="True" Background="Transparent"`. It supports per-pixel alpha transparency composited directly by DWM with zero glitches.
  - **Win32 Message Control**: Direct access to `HwndSource`, enabling seamless interception of `WM_MOUSEACTIVATE`, `WM_NCHITTEST`, `WM_HOTKEY`, and setting window styles (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`).
  - **Vector Graphics & Animations**: WPF's `StreamGeometry`, `PathGeometry`, `ArcSegment`, and `Storyboard` animation engine allow 100% mathematical fidelity to Pulse's `DockBerthShape`, `UsageBubbleShape`, and `UsageRingView`.
  - **Built-in .NET 10 Support**: .NET 10 SDK is already installed and verified on the target machine. Compiles natively to high-performance C# with AOT or single-file self-contained deployment.
  - **Native Windows Services**: Direct access to DPAPI (`System.Security.Cryptography.ProtectedData`), `Microsoft.Data.Sqlite`, `RegistryKey`, `Process`, and P/Invoke Win32 APIs.
  - **Tray Icon & Notifications**: Readily supported via `NotifyIcon` and Windows Toast Notifications.
- **Cons**:
  - Traditional WPF controls have an older look if unstyled (addressed by building modern Fluent/dark custom controls and glass styling).

### Option B: C# / WinUI 3 (Windows App SDK)

- **Pros**:
  - Modern Fluent controls out of the box.
- **Cons**:
  - **Severe Windowing Limitations**: WinUI 3 `AppWindow` does not natively support true `AllowsTransparency="True"` non-activating click-through windows without complex custom composition swap chains or undocumented DWM flags.
  - `WS_EX_NOACTIVATE` frequently breaks WinUI 3 input dispatch because XAML Islands expect a focused top-level window.
  - Requires Windows App SDK runtime installation or MSIX packaging overhead, complicating standalone CLI (`Pulse.exe --json`) usage.
  - Immature system tray story.

### Option C: Avalonia UI (.NET)

- **Pros**:
  - Cross-platform XAML framework.
- **Cons**:
  - Additional abstraction layer over Win32; non-activating tool window behavior across multiple monitors requires deep platform-specific hacks.
  - Less direct integration with native Windows DWM composition than WPF on Windows.

### Option D: Native C++ / Win32 / Direct2D

- **Pros**:
  - Minimal memory footprint, raw performance.
- **Cons**:
  - Immense development overhead for vector animations, layout, and JSON/HTTP handling.
  - Very slow iteration velocity.

### Option E: Swift on Windows + Separate UI

- **Pros**:
  - Shared Swift code between macOS and Windows.
- **Cons**:
  - Swift toolchain is not installed on standard Windows systems (`swift: command not found`).
  - No stable, production-ready GUI toolkit for Swift on Windows (SwiftUI and AppKit are Apple-proprietary; SwiftWin32 is an incomplete hobby project).
  - Building Swift C-interop and SPM on Windows is fragile and lacks standard CI runner support.

---

## 4. Decision

We choose **Option A: C# / .NET 10 with WPF and native Win32 interop**.

The solution architecture consists of:
1. **`Pulse.Core`** (.NET Class Library):
   - Domain models (`AccountKey`, `Provider`, `UsageWindow`, `ProviderUsage`, `MonitoredAccount`).
   - Provider service implementations & parsers (Codex, Claude Code, Cursor, Copilot, Antigravity, OpenCode, Kimi, Zhipu, DeepSeek, etc.).
   - Usage cache & JSON report generation (`Pulse --json`).
   - Platform abstractions (`IPlatformPaths`, `ICredentialStore`, `IProcessRunner`, `INotificationService`).
2. **`Pulse.Windows`** (.NET Windows Application):
   - `FloatingRailWindow`: Transparent, borderless, non-activating, always-on-top, docked/floating rail with custom vector geometry and smooth hover animations.
   - `DetailCardPopup` / overlay: Rich usage breakdown with progress rings, reset countdowns, and cost estimates.
   - System Tray component with context menu.
   - Settings window with provider management, API key entry, and appearance options.
   - Native implementations for DPAPI (`WindowsDPAPICredentialStore`), Windows paths (`WindowsPlatformPaths`), Hotkeys (`WindowsHotkeyService`), and Autostart (`WindowsStartupService`).

---

## 5. Consequences

### Positive
- Full fidelity to Pulse's signature floating rail UX, transparency, and silky-smooth animations.
- Exact non-activating window behavior: Pulse will never steal focus while typing in VS Code, Cursor, or Windows Terminal.
- Zero external runtime dependencies: runs directly on .NET 10.
- Safe, enterprise-grade credential storage via Windows DPAPI and Credential Manager.
- High developer productivity and rapid implementation of providers and UI.

### Negative / Mitigations
- Code between macOS (Swift) and Windows (C#) will be separate source trees, sharing protocol and data contracts rather than raw binary binaries.
  - *Mitigation*: Test suites will use the exact same JSON fixtures (`Tests/PulseTests/Fixtures/*.json`) to guarantee 100% parsing parity.
