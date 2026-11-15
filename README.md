# Miko — Lightweight Windows Desktop AI Companion

Miko is an ultra-lightweight, personal 2D cybernetic companion residing on your Windows desktop. She perches on top of your active application window title bars, follows their movements, reacts to your mouse actions, and converses through the Google Gemini REST API under strict, zero-trust security guardrails.

---

## ⚡ Performance Profile

- **Memory Usage**: **~5.8 MB RAM** working set (< 50 MB target)
- **Managed Heap**: **~0.43 MB**
- **Idle CPU**: **~0.0% to 0.5%** (automatically throttled animation timer and idle memory trimming)
- **Local Footprint**: Zero local LLM/TTS bloat — direct cloud REST API for intelligence.

---

## 🛡️ Security & Isolation Guardrails

1. **Zero External Code Execution & Zero Unverified DLLs**:
   - Strictly built using official Microsoft .NET 8 runtime libraries (zero third-party NuGet packages).
   - Only PNG image assets and plain-text/JSON configs are permitted in `./assets/` or `./config/`. No dynamic script compilers or remote code execution.
2. **Lockdown on System Tools (`ToolDispatcher.cs`)**:
   - **No Shell Access**: `cmd.exe`, `powershell.exe`, and arbitrary `Process.Start` shell invocations are completely removed and blocked.
   - **Strict Application Allowlist**: `open_app` can only launch pre-approved apps (`notepad`, `calculator`, `chrome`, `spotify`, `discord`, `vscode`, `paint`). All path traversal (`..`), directory separators, URLs, and unknown binaries are rejected.
   - **Human-in-the-Loop Confirmation**: Sensitive actions require explicit operator confirmation via a native Windows modal dialog (defaulting to Deny).
3. **Local-Only Access & Zero Network Listeners**:
   - Zero open ports, WebSockets, named pipes, or HTTP servers.
   - Outbound network traffic is cryptographically constrained strictly to `https://generativelanguage.googleapis.com` via TLS.
4. **API Key & Secrets Protection**:
   - `GEMINI_API_KEY` is redacted (`[REDACTED_API_KEY]`) before being displayed in speech bubbles or logs.
   - `config/appsettings.json` is ignored in `.gitignore`.
   - Access to sensitive folders (`C:\Windows`, `AppData`, `.ssh`, `.env`, browser credentials) is blocked.
5. **Prompt Injection Boundary (`GeminiClient.cs`)**:
   - System directives, user context, and security invariants are separated using isolated XML boundary tags (`<system_directives>`, `<user_context>`, `<security_guardrails>`). External user prompts cannot override system constraints.

---

## 🚀 Key Features

1. **Per-Pixel Alpha Transparent Layered Window (`UpdateLayeredWindow`)**
   - Borderless, top-most desktop overlay.
   - Click-through on transparent pixels (`WM_NCHITTEST` returning `HTTRANSPARENT`).
   - Non-transparent character pixels respond to drag and click events.
2. **2D Sprite Animation Engine**
   - Cycles through states: `Idle`, `Perching`, `Thinking`, `Talking`, `GlitchReact`, `Falling`, `Sleeping`.
   - Supports random blinking (`idle_blink`, `perch_blink`) and glitch reactions (`glitch`, `perch_glitch`).
3. **Window-Perching Gravity Physics**
   - Win32 DWM integration (`DwmGetWindowAttribute` with `DWMWA_EXTENDED_FRAME_BOUNDS`) detects exact window title bars.
   - Perches dynamically on active application windows and moves synchronously with them.
   - When dragged and released in mid-air, gravity takes over and she falls smoothly to land on the nearest window or desktop floor.
4. **Google Gemini REST API Brain**
   - Direct HTTP client to `generativelanguage.googleapis.com`.
   - Allowed tool calls:
     - `open_app(app_name)` (allowlisted only, with operator prompt)
     - `control_media(action)`
     - `get_active_window()`
     - `set_character_state(state)`
   - Personality loaded from `config/personality.md` and user profile from `config/me.md`.
5. **Cyberpunk Minimalist UI**
   - Floating speech bubble with typewriter text animation.
   - Global hotkey `Ctrl + Alt + T` to summon the direct input dialog.
   - System tray icon with quick actions and state toggles.

---

## 🛠️ Quick Start

### Build & Run
```powershell
# Build Release
dotnet build Miko.slnx -c Release

# Run self-test suite, security guardrail tests, and benchmarks
dotnet run --project src/Miko/Miko.csproj -c Release -- --test

# Launch Miko
dotnet run --project src/Miko/Miko.csproj -c Release
```

### Configuration
Edit `config/appsettings.json`:
```json
{
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "Model": "gemini-3.5-flash-lite",
    "AvailableModels": [
      "gemini-3.5-flash-lite",
      "gemini-3.5-flash",
      "gemini-3.8-flash"
    ]
  },
  "Companion": {
    "Size": 240
  },
  "Security": {
    "RequireConfirmation": true,
    "AllowedApps": {
      "notepad": "notepad.exe",
      "calculator": "calc.exe",
      "chrome": "chrome.exe",
      "spotify": "spotify.exe",
      "discord": "discord.exe",
      "vscode": "code.cmd",
      "paint": "mspaint.exe"
    }
  }
}
```
*Alternatively, set the environment variable:*
```powershell
$env:GEMINI_API_KEY = "your-api-key-here"
```

---

## 🎮 Controls & Shortcuts

| Action | Control |
|---|---|
| **Chat Dialog** | `Ctrl + Alt + T` or Right-Click > Talk to Miko |
| **Move Miko** | Left-Click & Drag (releases into gravity fall) |
| **Context Menu** | Right-Click on character |
| **Drop Character** | Context Menu > Drop (Trigger Gravity) |
| **State Switching** | Context Menu > Animation State |
| **Exit** | Context Menu or Tray Icon > Exit Miko |
