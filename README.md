# LoopcastUA

[日本語](README.ja.md)

A system-tray Windows client that continuously streams PC audio output to a FreePBX conference bridge over SIP/RTP.

---

## Overview

LoopcastUA captures whatever is playing on a Windows PC (notification sounds, announcements, etc.), encodes it with the Opus codec, and sends it to a FreePBX ConfBridge — allowing 20–40 machines to feed a shared audio stream simultaneously.

```
[PC-A] LoopcastUA ─┐
[PC-B] LoopcastUA ─┤  SIP/RTP (Opus)  ┌─────────────────┐
[PC-C] LoopcastUA ─┼─────────────────▶│ FreePBX         │──▶ Listeners
       ...         │                  │ ConfBridge 8000 │
[PC-N] LoopcastUA ─┘                  └─────────────────┘
```

**Key features:**

- WASAPI loopback capture — grabs the PC's audio output directly, no virtual cable needed
- Opus 48 kHz / 48 kbps encoding for low-latency, low-bandwidth delivery
- Silence detection with hysteresis — runs a configurable batch/script on playback start and stop
- Exponential backoff auto-reconnect on disconnect
- System-tray icon shows connection state at a glance (yellow = connecting / green = connected / green+waves = playing / red = error)
- EN / JA bilingual UI — follows system language automatically, with a manual override in Settings
- MSI installer with silent-install support for GPO mass deployment

---

## Requirements

### Client (LoopcastUA)

| Item | Requirement |
|---|---|
| OS | Windows 7 SP1 or later (64-bit) |
| Runtime | .NET Framework 4.7.2 or later |
| Audio | WASAPI loopback-capable audio device |
| Network | UDP 5060 (SIP) and UDP 10000+ (RTP) reachable to the FreePBX server |

### Server (FreePBX)

| Item | Recommended |
|---|---|
| Distribution | FreePBX 17 (Debian 12-based) |
| Asterisk | 20 (LTS) |
| vCPU | 4 |
| RAM | 4 GB |
| NIC | 100 Mbps |

See [docs/freepbx-runbook.md](docs/freepbx-runbook.md) for server setup instructions.

---

## Building

### Prerequisites

| Tool | Where to get it |
|---|---|
| Build Tools for Visual Studio 2022+ | [visualstudio.microsoft.com](https://visualstudio.microsoft.com/downloads/) — select the **.NET desktop build tools** workload |
| NuGet CLI (`nuget.exe`) | [nuget.org/downloads](https://www.nuget.org/downloads) — place in `C:\tools\` and add to PATH |
| WiX Toolset v3.14 (MSI only) | [github.com/wixtoolset/wix3/releases](https://github.com/wixtoolset/wix3/releases) |

### Build the EXE

```powershell
cd client
nuget restore LoopcastUA.sln
msbuild LoopcastUA.sln /p:Configuration=Release "/p:Platform=Any CPU"
# Output: client/LoopcastUA/bin/Release/LoopcastUA.exe
```

### Build the MSI

```powershell
# Run after building the EXE
cd installer
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.0
# Output: installer/bin/Release/LoopcastUA-1.0.0.msi
```

---

## Installation

### Standard install (GUI)

Double-click `LoopcastUA-x.y.z.msi` and follow the wizard.

On first launch, the Settings dialog opens automatically. Enter your SIP server details, click Save, and the client starts connecting.

### Silent install (mass deployment)

```cmd
msiexec /i LoopcastUA-1.0.0.msi /qn
```

After installation, place a per-machine config file at:

```
%ProgramData%\LoopcastUA\config.json
```

### Upgrade

Run the new MSI directly — the old version is uninstalled automatically before the new one is installed. `config.json` is preserved.

---

## Configuration

**You do not need to edit any config files for normal use.**  
On first launch after installation, the Settings dialog opens automatically. Enter your SIP server details, click Save, and the client connects. You can reopen Settings at any time from the tray icon's right-click menu.

Settings are saved automatically to `%ProgramData%\LoopcastUA\config.json`.

---

### Config file reference (advanced / mass deployment)

This section is for administrators who need to pre-deploy `config.json` via silent install or generate per-machine files via script. A template is available at [installer/config.template.json](installer/config.template.json).

### Main settings

```jsonc
{
  "sip": {
    "server": "192.168.11.29",       // FreePBX server IP or hostname
    "port": 5060,
    "username": "9001",              // Extension number
    "password": "DPAPI:AQAAn...",    // DPAPI-encrypted password (plaintext also accepted; see below)
    "conferenceRoom": "8000",        // ConfBridge number
    "displayName": "PC-A-Loopback"
  },
  "audio": {
    "opusBitrate": 48000             // bps (8000–128000)
  },
  "silenceDetection": {
    "thresholdDbfs": -50.0,          // Silence threshold
    "enterSilenceMs": 1500,          // How long below threshold before "silence" is declared
    "exitSilenceMs": 300             // How long above threshold before "playing" is declared
  },
  "batch": {
    "onPlaybackStart": "C:\\scripts\\on_start.bat",
    "onPlaybackStop":  "C:\\scripts\\on_stop.bat",
    "executionTimeoutMs": 5000
  }
}
```

### Password encryption

Saving via the Settings dialog encrypts the password automatically using DPAPI (Windows Data Protection API).  
To encrypt manually from the command line:

```cmd
"C:\Program Files\LoopcastUA\LoopcastUA.exe" --encrypt-config
```

For testing/debugging you can store the password in plaintext by adding `"passwordPlaintext": true`.

### Bulk config deployment

For large-scale silent deployments, prepare a per-machine `config.json` and place it in `%ProgramData%\LoopcastUA\` on each PC. A common approach is to generate the files from the same CSV used to bulk-import extensions in FreePBX Bulk Handler.

---

## Silence detection and batch execution

LoopcastUA can run an arbitrary script whenever playback state changes.

| Event | Config key | Example use |
|---|---|---|
| Playback starts (silence → audio) | `batch.onPlaybackStart` | Turn on lights, wake screen, send notification |
| Playback stops (audio → silence) | `batch.onPlaybackStop` | Turn off lights, blank screen |

Supports `.bat`, `.cmd`, `.exe`, and `.ps1`. Use `executionTimeoutMs` to cap execution time.

---

## Project structure

```
loopcast/
├── client/
│   ├── LoopcastUA.sln
│   └── LoopcastUA/
│       ├── LoopcastUA.csproj
│       ├── config.sample.json
│       └── src/
│           ├── Program.cs              # Entry point
│           ├── TrayContext.cs          # Tray icon and lifecycle management
│           ├── Forms/
│           │   └── SettingsForm.cs     # Settings dialog (code-first WinForms)
│           ├── Audio/
│           │   ├── LoopbackCapturer.cs # WASAPI loopback capture
│           │   ├── AudioMixer.cs       # Stereo → mono downmix
│           │   ├── OpusEncoder.cs      # Concentus (pure C# Opus)
│           │   └── SilenceDetector.cs  # Hysteresis-based silence detection
│           ├── Sip/
│           │   ├── SipClient.cs        # SIPSorcery control and reconnect logic
│           │   └── RtpSender.cs        # RTP transmission
│           ├── Config/
│           │   ├── AppConfig.cs        # Settings POCO
│           │   ├── ConfigStore.cs      # Read/write and ConfigChanged event
│           │   ├── ConfigValidator.cs  # Validation
│           │   └── DpapiProtector.cs   # Password encryption
│           ├── Batch/
│           │   └── BatchRunner.cs      # Script execution with timeout
│           └── Infrastructure/
│               ├── Logger.cs           # Rolling file logger
│               ├── BufferPool.cs       # byte[] pool
│               └── Strings.cs          # i18n strings (EN / JA)
├── installer/
│   ├── Product.wxs                     # WiX v3 MSI definition
│   ├── build.ps1                       # Build script
│   └── config.template.json            # Config file template
├── docs/
│   └── freepbx-runbook.md              # FreePBX setup guide
└── tools/
    └── generate_extensions.py          # FreePBX extension CSV generator (planned)
```

---

## Third-party libraries

| Library | Version | License | Purpose |
|---|---|---|---|
| [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) | 10.0.3 | BSD-3-Clause + additional clause ([details](THIRD_PARTY_NOTICES.md)) | SIP / RTP stack |
| [NAudio](https://github.com/naudio/NAudio) | 2.3.0 | MIT | WASAPI loopback capture |
| [Concentus](https://github.com/lostromb/concentus) | 2.2.2 | BSD-3-Clause | Opus encoder (pure C#) |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0.3 | MIT | JSON config read/write |

---

## License

[MIT License](LICENSE)

For dependency licenses and copyright notices, see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
