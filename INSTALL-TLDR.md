# Install TL;DR — Thales Scanner Bridge

**What the client needs to install this bridge.**

On the **Windows x64 PC where the scanner is plugged in**:

1. Download **[`ThalesBridgeSetup.exe`](https://bitbucket.org/solaireresortcasino/thales-scanner-bridge/downloads/ThalesBridgeSetup.exe)** from Bitbucket Downloads.
2. Double-click it and approve the Administrator prompt.
3. Select the licensed Thales SDK `.msi` if the SDK is not already installed.
4. Click **Install**.

The client does not need to open PowerShell or install the .NET SDK.

## Hard prerequisites

The installer **cannot** work around any of these.

| # | Need | Why |
|---|------|-----|
| 1 | Windows PC, x64 | Native SDK DLLs + reader driver are Windows-only. No macOS/Linux runtime. |
| 2 | **Thales Document Reader SDK x64 `.msi`** (e.g. `3.9.2.49`) | Installs USB drivers **and** the native DLLs the bridge calls at runtime. Licensed/proprietary — **not bundled**, obtain your own. The installer hard-stops without it. |
| 3 | The reader plugged in (QS2000 or another FullPage-API model) | Nothing to scan otherwise. The bridge starts anyway and retries every 10s until the reader appears. |
| 4 | Permission to approve a Windows Administrator prompt | Needed to install the MSI, write to `C:\Program Files`, add the URL ACL, and register the Scheduled Task. |

## Not required

- **.NET 8 SDK** — only needed to build from source. The released `.exe` and `publish.zip` are self-contained.

## After install

- The bridge runs **at boot as SYSTEM** (no interactive logon needed).
- It serves `ws://localhost:8765`, emitting `DocumentScanResult` JSON to any WebSocket client — see [`CONTRACT.md`](CONTRACT.md).
- To remove it: `setup.ps1 -Uninstall` (or run `uninstall.ps1`).
