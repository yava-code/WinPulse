# Provider Compatibility Matrix: Pulse for Windows

This document maps all 20 quota providers supported by Pulse, along with token-spend sources, detailing their authentication routes, macOS dependencies, expected Windows paths/APIs, portable logic, and MVP readiness.

---

## 1. Quota Providers Compatibility Matrix

| Provider | Current Auth/Data Route | macOS Dependency | Expected Windows Path/API | Portable Logic | Required Windows Work | Expected Difficulty | Can Ship in Windows MVP |
|---|---|---|---|---|---|---|---|
| **Codex** (`.codex`) | Reads `~/.codex/auth.json`, HTTPS to `chatgpt.com/backend-api/wham/usage`; fallback to `codex app-server` | `NSHomeDirectory()` | `%USERPROFILE%\.codex\auth.json` | Token extraction, HTTP request headers, response JSON parsing, group-spent marking | Path resolution (`IPlatformPaths`), HTTP client, JSON parser | Low | **Yes** (Priority 1) |
| **Claude Code** (`.claudeCode`) | Primary: `security find-generic-password` or `~/.claude/.credentials.json`, HTTPS `api.anthropic.com/api/oauth/usage`; fallback: status-line hook | `/usr/bin/security`, macOS Keychain, status-line hook cache | `%USERPROFILE%\.claude\.credentials.json` or Windows Credential Manager (`Claude Code-credentials`) | Endpoint parsing, 5h & weekly window model, refresh logic | Credential lookup (file + CredRead), HTTP fetch, optional status-line hook | Low to Medium | **Yes** (Priority 2) |
| **Cursor** (`.cursor`) | Reads JWT from SQLite `state.vscdb`, constructs `WorkosCursorSessionToken`, HTTPS `cursor.com/api/usage` | `~/Library/Application Support/Cursor/User/globalStorage/state.vscdb` | `%APPDATA%\Cursor\User\globalStorage\state.vscdb` | SQLite query (`ItemTable` -> `cursorAuth/accessToken`), JWT `sub` & `exp` claims decoding, cookie formatting, usage JSON parsing | Read-only SQLite connection, Windows path resolution | Low to Medium | **Yes** (Priority 3) |
| **GitHub Copilot** (`.copilot`) | GitHub Device OAuth flow, token stored in `keys.dat`, HTTPS `api.github.com` | Keychain / IOKit machine UUID for `keys.dat` | DPAPI encrypted `keys.dat` in `%LOCALAPPDATA%\Pulse\keys.dat`, HTTPS API | Device code grant flow, token polling, Copilot quota JSON parser | OAuth device flow UI/loop, DPAPI token store | Low | **Yes** (Priority 4) |
| **Antigravity** (`.antigravity`) | Loopback HTTPS to local `language_server` with `--csrf_token` and random port | `/bin/ps`, `/usr/sbin/lsof`, bundle search `/Antigravity.app/` | `Process.GetProcessesByName("language_server_windows_x64")`, inspect command line for `--csrf_token`, query listening ports via TCP table | RPC method `RetrieveUserQuotaSummary` and `GetUserStatus`, protobuf/JSON parsing, Gemini & Claude/GPT scoping | Process enumeration (`WMI` or Win32 `QueryFullProcessImageName`/PEB), TCP listening port enumeration (`GetExtendedTcpTable`), HTTPS self-signed client | Medium | **Yes** (Post-bootstrap) |
| **Grok** (`.grok`) | Reads `~/.grok/auth.json`, HTTPS endpoint | `NSHomeDirectory()` | `%USERPROFILE%\.grok\auth.json` | Auth token extraction, HTTP request, rate limit parsing | File lookup, HTTP client | Low | **Yes** |
| **Grok Bot** (`.grokBot`) | Uses Cursor cookie from `state.vscdb`, HTTPS | Cursor macOS path | `%APPDATA%\Cursor\User\globalStorage\state.vscdb` | Cursor cookie reuse, Grok Bot usage endpoint parsing | Shared with Cursor auth | Low | **Yes** |
| **OpenCode Go** (`.openCodeGo`) | Pasted key or OpenCode `auth.json`, HTTPS | `~/.config/opencode` or `auth.json` | `%USERPROFILE%\.opencode\auth.json` or `%APPDATA%\opencode\auth.json` | Key validation, HTTP request, balance & quota parsing | Path resolution, DPAPI store for pasted keys | Low | **Yes** |
| **Kimi Code** (`.kimiCode`) | Pasted API key, HTTPS `api.moonshot.cn` | None | API key in DPAPI store | Header construction, rate limit parsing (rolling week) | DPAPI key store, HTTP client | Low | **Yes** |
| **z.ai** (`.zai`) | Pasted API key, HTTPS `api.z.ai` | None | API key in DPAPI store | Usage JSON parsing, history parsing | DPAPI key store, HTTP client | Low | **Yes** |
| **Zhipu / GLM** (`.glmCoding`) | Pasted key or mainland GLM key files, HTTPS | Mainland path checks | `%USERPROFILE%\.zhipu\key` or DPAPI key store | Quota and model usage JSON parsing | Path resolution, HTTP client | Low | **Yes** |
| **MiniMax** (`.minimax`) | Pasted API key, HTTPS | None | API key in DPAPI store | Quota JSON parsing, remaining inversion | DPAPI key store, HTTP client | Low | **Yes** |
| **MiniMax CN** (`.minimaxCN`) | Pasted API key, HTTPS mainland | None | API key in DPAPI store | Shared MiniMax parser | DPAPI key store, HTTP client | Low | **Yes** |
| **Command Code** (`.commandCode`) | Pasted key or `~/.commandcode/auth.json`, HTTPS | `NSHomeDirectory()` | `%USERPROFILE%\.commandcode\auth.json` | Monthly plan grant estimation, credit balance pool parsing | Path resolution, HTTP client | Low | **Yes** |
| **DeepSeek** (`.deepSeek`) | Pasted API key, HTTPS `api.deepseek.com/user/balance` | None | API key in DPAPI store | Balance parsing, currency denomination, user budget denominator | DPAPI key store, HTTP client | Low | **Yes** |
| **Devin** (`.devin`) | Chromium `localStorage` or pasted `token org`, HTTPS | macOS Chromium LevelDB paths | `%LOCALAPPDATA%\Google\Chrome\User Data\Default\Local Storage\leveldb` or `%APPDATA%\Devin` | LevelDB key extraction or pasted token, quota parsing | Chromium LevelDB reader on Windows, fallback pasted key | Medium | **Yes** (Pasted key) / Planned (LevelDB) |
| **Ollama Cloud** (`.ollamaCloud`) | Browser session cookie from Chrome/Edge/Firefox/Safari | macOS Keychain `Chrome Safe Storage`, `binarycookies` | Windows Chromium `Local State` (DPAPI master key) + SQLite cookies `v10` decryption; Firefox unencrypted SQLite | Session cookie filtering, Ollama HTML/API quota parsing | Chromium DPAPI master key unprotect + AES-GCM cookie decrypt, Firefox cookie reader | Medium | **Yes** (Firefox / pasted session) / Phase 2 (Chromium DPAPI) |
| **Xiaomi Coding Plan** (`.xiaomiMiMo`) | Browser session cookie or pasted `Cookie:` header, HTTPS | macOS browser cookie import | Same as Ollama Cloud / pasted cookie header | Xiaomi platform quota & balance JSON parsing | Shared browser cookie reader or manual input | Medium | **Yes** (Manual) / Phase 2 (Auto) |
| **Volcengine** (`.volcengine`) | `arkcli` execution or pasted AK:SK signed requests | `Process()` calling `arkcli` | `%USERPROFILE%\.arkcli` or `arkcli.exe` on PATH, DPAPI AK:SK | V4 HMAC-SHA256 request signing, quota parsing | V4 signer in C#, process runner | Low to Medium | **Yes** |
| **Kiro** (`.kiro`) | Kiro CLI ACP client process communication | macOS binary path | `kiro.exe` on Windows PATH or `%LOCALAPPDATA%\Programs\Kiro` | ACP JSON-RPC over stdin/stdout | Windows child process IPC (`AnonymousPipeClientStream`) | Medium | Phase 2 |

---

## 2. Token Spend Sources Compatibility (Groups A - F)

Pulse monitors 54 local agent log stores for token expenditure. On Windows, the storage mechanisms translate as follows:

| Group | Category | Typical macOS Location | Windows Target Location | Format | Windows Parser Compatibility |
|---|---|---|---|---|---|
| **Group A** | Session Logs (Pi, Omp, Senpi, Gemini, Qwen, Amp, Droid, OpenClaw) | `~/.pi`, `~/.gemini/tmp`, `~/.qwen` | `%USERPROFILE%\.pi`, `%USERPROFILE%\.gemini\tmp`, `%USERPROFILE%\.qwen` | JSONL & SQLite | **100% Portable**; identical JSONL streaming parser |
| **Group B** | Editor Logs (RooCode, KiloCode, Cline, CodeBuddy, WorkBuddy, CherryStudio, CommandCode, OpenCodeReview, ZCode) | `~/Library/Application Support/Code/User/globalStorage/...` | `%APPDATA%\Code\User\globalStorage\...`, `%APPDATA%\Cursor\...` | JSONL, JSON, SQLite | **100% Portable**; simple root directory redirection via `IPlatformPaths` |
| **Group C** | Databases & Event Logs (Hermes, Goose, Zed, Kiro, Unsloth, Antigravity CLI/IDE, Devin Desktop) | `~/Library/Application Support/goose/...`, `~/.gemini/antigravity/...` | `%APPDATA%\goose\sessions\sessions.db`, `%USERPROFILE%\.gemini\antigravity-ide\conversations\*.db` | SQLite, NDJSON | **100% Portable**; read-only SQLite with WAL support |
| **Group D** | Structured Logs (Mux, Codebuff, Jcode, Augment, GJC, Junie, DSH, FX, LMStudio, Reasonix) | `~/.jcode`, `~/.lmstudio/server-logs` | `%USERPROFILE%\.jcode`, `%USERPROFILE%\.lmstudio\server-logs` | JSON, JSONL | **100% Portable** |
| **Group E** | Captured Usage (Cursor, Antigravity, Trae, Warp, Hindsight, Mcode) | `~/.config/tokscale`, `~/Library/Application Support/Pulse/UsageImports` | `%APPDATA%\Pulse\UsageImports`, `%USERPROFILE%\.config\tokscale` | JSON, CSV, JSONL | **100% Portable** |
| **Group F** | Legacy Transcripts & Copilot (Claude Code, Codex, Copilot) | `~/.claude/projects`, `~/.codex/sessions` | `%USERPROFILE%\.claude\projects`, `%USERPROFILE%\.codex\sessions` | JSONL, OTEL, SQLite | **100% Portable** |

---

## 3. Recommended Provider Rollout Sequence

1. **Milestone 1 (MVP)**:
   - **Codex**: Primary route via `%USERPROFILE%\.codex\auth.json` (instant, robust, zero-friction).
   - **Claude Code**: Primary route via `%USERPROFILE%\.claude\.credentials.json` and Windows Credential Manager.
   - **Cursor**: Read-only SQLite query to `%APPDATA%\Cursor\User\globalStorage\state.vscdb`.
   - **API-Key Providers**: DeepSeek, Kimi Code, z.ai, OpenCode Go, MiniMax (instant setup via Settings).
2. **Milestone 2**:
   - **GitHub Copilot**: Device code OAuth flow and token persistence in DPAPI store.
   - **Antigravity**: Windows process and TCP port enumeration (`language_server_windows_x64.exe`).
   - **Grok & Command Code**: Auth JSON file discovery.
3. **Milestone 3**:
   - **Chromium / Edge Cookie Reader**: DPAPI master key unprotection + AES-256-GCM cookie decryption (Ollama Cloud, Xiaomi).
   - **Kiro**: Windows ACP client process runner.
   - **Token Spend Store Scanning**: Windows background file scanner across Group A-F.
