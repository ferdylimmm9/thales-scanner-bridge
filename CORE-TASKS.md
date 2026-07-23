# Core Tasks — Why This Repo Exists

**thales-scanner-bridge** is a WebSocket bridge for the **Thales Gemalto Document Reader QS2000**. It turns passport / ID / MRZ scans into JSON that any web app can consume over `ws://localhost`.

```
[Thales QS2000] --USB--> [ThalesBridge.exe] --ws://localhost:8765--> [your web app]
     driver              wraps MMMReaderDotNet50.dll                  any WebSocket client
                         emits DocumentScanResult JSON                (see CONTRACT.md)
```

## The problem it solves

The Thales reader ships with a **Windows-only, native FullPage SDK**. Web apps (React, kiosk front-ends, etc.) cannot call native DLLs or USB hardware directly from the browser. This repo is the bridge in between: a small Windows service that talks to the SDK on one side and speaks plain WebSocket JSON on the other — so any browser app can read documents without knowing anything about Thales, C#, or USB.

## Core tasks

| # | Task | What it does |
|---|------|--------------|
| 1 | **Wrap the Thales SDK** | Load `MMMReaderDotNet50.dll`, initialise the reader, drive a scan, and pull the parsed document data (MRZ, ePassport, ID fields). |
| 2 | **Normalise to a stable contract** | Convert raw SDK output into a documented, language-neutral `DocumentScanResult` JSON — see `CONTRACT.md` + the JSON Schema in `contract/`. Client apps depend on the contract, not the SDK. |
| 3 | **Serve over WebSocket** | Expose `ws://localhost:8765` and push scan results to any connected client in real time. |
| 4 | **Survive real deployments** | Retry every 10s until the reader appears; run at boot as SYSTEM so a kiosk PC serves from power-on with no logon. |
| 5 | **Install with one executable** | `ThalesBridgeSetup.exe` provides a double-click installer with SDK selection, configuration, and a live log — no clone, shell commands, or dev tools on the target PC. PowerShell scripts remain available for advanced diagnostics and uninstalling. |
| 6 | **Stay buildable anywhere** | The managed wrapper DLL is checked in so the project *compiles* on macOS/Linux/CI; real *runs* require the licensed SDK on Windows. |

## Who touches what

- **Integrating a client app?** You only need `CONTRACT.md` — the WebSocket data contract. (For React, use the `thales-scanner-client` package, which already implements it.)
- **Installing on the scanner PC?** You only need `INSTALL-TLDR.md`.
- **Building/operating the bridge itself?** See `README.md`.

## Hard constraints

- **Windows x64 only** at runtime — the SDK's native DLLs and the reader driver are Windows-only.
- **The Thales SDK `.msi` is licensed and never bundled** — each deployment must supply its own; the bridge cannot run without it.
- Verified on the **QS2000**; likely compatible with other FullPage-API readers (AT9000 MK2, AT10K, CR5400, KR9000) but not physically confirmed.
