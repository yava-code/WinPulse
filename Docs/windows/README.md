# Pulse for Windows

Welcome to the Windows port of **Pulse**, the real-time AI quota and token-spend monitor.

Pulse provides an ambient, distraction-free floating rail that keeps you aware of your usage limits across multiple AI coding assistants and services without breaking your flow.

---

## Key Highlights

- **Floating, Edge-Docked Rail**: Sits discreetly on your screen edge (Left, Right, or Top) or floats anywhere you place it.
- **Auto-Collapse & Expand**: Silently collapses into a 6px sliver at rest and smoothly expands on hover with spring physics.
- **Non-Activating Window**: Uses native Win32 window properties (`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WM_MOUSEACTIVATE`) so interacting with rings or details never steals focus from your editor or terminal.
- **System Tray Integration**: Quiet background operation with quick access to Settings, Refresh, and Quit.
- **Local & Secure**: No backend, no accounts, no telemetry. Credentials and keys are protected using Windows DPAPI (`CryptProtectData`).
- **20 Supported Quota Providers**: Codex, Claude Code, Cursor, GitHub Copilot, Antigravity, DeepSeek, Kimi, z.ai, and more.
- **CLI Mode**: Run `Pulse.exe --json` to extract usage snapshots for your terminal prompt or external tools.

---

## Documentation Index

- [ADR-001: Technology Stack Selection](ADR-001-windows-stack.md)
- [Provider Compatibility Matrix](PROVIDER_COMPATIBILITY.md)
- [Porting & Implementation Plan](PORTING_PLAN.md)
- [Known Gaps & Architecture Differences](KNOWN_GAPS.md)
- [Current Implementation Status](STATUS.md)

---

## Building and Running on Windows

### Prerequisites
- Windows 10 (version 1809+) or Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/) (x64).

### Build Commands

From the repository root:

```powershell
# Build entire Windows solution
dotnet build Windows/Pulse.sln -c Release

# Run tests
dotnet test Windows/Pulse.sln

# Launch Pulse for Windows
dotnet run --project Windows/Pulse.Windows/Pulse.Windows.csproj

# Run CLI JSON mode
dotnet run --project Windows/Pulse.Windows/Pulse.Windows.csproj -- --json
```
