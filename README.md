# thales-scanner-bridge

## Secure desktop release

The bridge now uses **`wss://localhost:8765/`**. Update the frontend's
`NEXT_PUBLIC_THALES_BRIDGE_URL` to this URL and rebuild/redeploy that frontend.
The frontend lives in a separate repository and is not updated by this installer.
Do not use `127.0.0.1`: the certificate is issued for `localhost`.

Run the new **ThalesBridgeSetup.exe** as administrator on each Windows scanner PC,
including PCs with an older bridge installed. Setup installs a unique, non-exportable
localhost TLS key, trusts its public certificate on that PC, configures HTTP.sys,
and installs **Thales Scanner Bridge** shortcuts on the desktop and Start menu.
Changing the URL without rerunning setup will not enable TLS.
This follows Microsoft's [HttpListener certificate requirements](https://learn.microsoft.com/en-us/dotnet/api/system.net.httplistener)
and [HTTP.sys SSL binding configuration](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http).

The desktop manager opens after installation and provides:

- Live WSS status, connection attempts, successful connections, received frame count and last frame time.
- Thales reader status, completed scans, last scan time, and retained last error.
- Start, Stop, Restart, Enable auto-start, and Diagnostics controls.
- Editable local secure endpoint with validation, certificate binding updates, restart/handshake verification, and rollback on verification failure.
- Soft status colors and a moving progress indicator while the reader is scanning.
- Diagnostics for TLS trust/expiry/binding, port ownership, task execution, bridge version/process,
  SDK location/version, capture settings, and matching Windows device names.

Manager counters cover its current session. Device-name detection is best effort;
reader readiness comes from live SDK events. Document photos and identity fields are
not displayed in the manager. Diagnostics run on opening and on demand; live status
updates continuously. The manager requests Windows administrator access for task management.
Closing it leaves the bridge running. Stop stops the current run; automatic startup
remains enabled. Start at boot is the default, with three one-minute restart attempts
on process failure. After those attempts, use Restart or inspect Diagnostics.
A computer that is powered off cannot serve requests; the bridge starts after Windows boots.

The local certificate expires after two years. Re-run setup within 30 days of expiry
to renew it. Browsers managed with separate trust policies may need IT to allow this
machine's certificate. No certificate-validation bypass is used.

For deployment verification, reboot the Windows PC, open the manager, confirm WSS and
reader readiness, scan/remove/reinsert a document several times, unplug/replug USB,
then restart the bridge while the frontend is open. The frontend must reconnect on
close/error (recommended bounded exponential backoff); that client code is outside
this repository. A healthy WSS connection alone does not prove optical scanning works.
If repeat scans fail, fully remove the document before replacing it, close other SDK
applications, and inspect the reader error and SDK settings. Hardware/placement issues
must be reproduced on a reader; they cannot be verified by this build.

Build all Windows executables: `./build-release.sh 0.0.0-wss-preview.1` or
`./build-release.ps1 -Version 0.0.0-wss-preview.1`.
Run transport regression checks: `dotnet run --project tests/TransportChecks.csproj`.



WebSocket bridge for the **Thales Gemalto Document Reader QS2000** — turns
MRZ / ePassport / ID scans into JSON over `wss://localhost` for any web app.
Built on the Thales FullPage SDK; likely compatible with other FullPage-API
readers (AT9000 MK2, AT10K, CR5400, KR9000, ...) though only the QS2000 has
been physically verified — see [Supported hardware](#supported-hardware).

> **Integrating a client app?** You only need [`CONTRACT.md`](CONTRACT.md) — the
> language-neutral WebSocket data contract (plus a JSON Schema in `contract/`).
> Building a React app specifically? Use
> [`thales-scanner-client`](https://github.com/ferdylimmm9/thales-scanner-client)
> instead of hand-rolling a WebSocket client — it implements this contract already.
> This README covers building and operating the bridge itself.

```
[Thales QS2000] --USB--> [ThalesBridge.exe]  --wss://localhost:8765-->  [your web app]
   driver               wraps MMMReaderDotNet50.dll,                   any WebSocket client
                        emits DocumentScanResult JSON                   (see CONTRACT.md)
```

> **This project only runs on the Windows PC where the scanner is plugged in** — the SDK's
> native DLLs and the reader hardware are Windows-only. It can be *compile-checked* on
> macOS/Linux with `dotnet build -c Release -p:EnableWindowsTargeting=true` (given
> `MMMReaderDotNet50.dll` in `libs/`). Bitbucket Pipelines cross-builds the Windows
> executable; hardware/runtime verification still requires a Windows scanner PC.

## Installation

### Before installing

Prepare these items on the Windows PC connected to the scanner:

| Required | Description |
|---|---|
| **Windows x64 PC** | The Thales driver and native SDK only run on Windows. |
| **Administrator access** | Setup needs approval to install under `C:\Program Files`, configure localhost access, and create the startup task. |
| **Thales Document Reader SDK x64** | It may already be installed. Otherwise, have the licensed SDK `.msi` ready, for example `Thales Document Reader SDK x64 3.9.2.49.msi`. |
| **Thales reader and USB cable** | The bridge can be installed before connecting the reader, but a physical scan cannot be verified until it is connected. |

The released installer is self-contained, so the client does **not** need the .NET SDK
and does **not** need to enter installation commands.

> The licensed Thales SDK is not included in this repository or inside
> `ThalesBridgeSetup.exe`. Obtain it through the reader purchase, the organization's
> Thales contact, or the team responsible for the scanner hardware.

### Get the installation files

For QA testing, an authorized repository member can open **Bitbucket → Pipelines**,
select a successful run for the `qa` branch, and download these artifacts:

- `ThalesBridgeSetup.exe` — the recommended offline installer;
- `monitor-bridge.bat` — optional live connection and scan-activity monitor; and
- `publish.zip` — advanced/manual deployment package.

This is a private repository. A user without repository permission cannot download its
files directly, even if given the Bitbucket URL. An authorized team member must first
download the artifacts and provide `ThalesBridgeSetup.exe` through an approved internal
file share or removable drive. Once copied to the scanner PC, the installer does not
need Bitbucket access.

### Install with `ThalesBridgeSetup.exe` (recommended)

1. Copy `ThalesBridgeSetup.exe` and, when needed, the licensed Thales SDK `.msi` to the
   scanner PC.
2. Double-click `ThalesBridgeSetup.exe` and approve the Windows Administrator prompt.
3. Confirm that the installer says **Thales Document Reader SDK detected**. If it is not
   detected, click **Browse** and select the SDK `.msi`.
4. Keep port **8765** unless the client application is configured to use another port.
5. For a QS2000, keep **Disable UV/IR capture** selected.
6. Keep boot startup selected for a shared kiosk. Select **Start at user logon** only if
   the scanner driver cannot operate before a user signs in.
7. Click **Install** and wait for **Installed successfully**.

The installer copies the bridge to `C:\Program Files\ThalesBridge`, configures it to
start automatically, starts it immediately, and checks the local WebSocket port.

### Confirm the installation

Keep the setup window open after installation and check **Live connection and activity**:

| Display | Meaning |
|---|---|
| `Bridge: connected on port 8765` | The local WebSocket bridge is running. |
| `Scanner: connected and ready` | The reader is detected and waiting for a document. |
| `Scanner: reading document` | A document was detected and capture is in progress. |
| `Activity: scan completed ...` | The bridge received and published a scan result. |

Place a supported document on the reader. A successful test changes from **reading
document** to **scan completed**, then returns to **connected and ready**.

After closing setup, double-click [`monitor-bridge.bat`](monitor-bridge.bat) to continue
monitoring `wss://localhost:8765/`. It reports status, completed scans, and errors without
printing names, document numbers, or document images. Press `Ctrl+C` to stop it. The
window remains open after a connection or script error so the message can be reviewed.

If the bridge is connected but the scanner is not detected:

1. Confirm the scanner has power and its USB cable is connected.
2. Close ReaderExpo, the Thales SDK demo, and any second bridge process—the SDK permits
   only one reader client at a time.
3. Confirm the reader works in the Thales SDK demo, close the demo, and restart the PC.
4. Review [Troubleshooting](#known-issue-error_camera_driver_error-at-startup) if it
   still does not connect.

## Command-line installation (maintainers only)

The executable is the supported end-user installation method. For maintenance or
diagnostics from a repository checkout:

```powershell
# Run in an elevated PowerShell from the repository root.
powershell -ExecutionPolicy Bypass -File setup.ps1
powershell -ExecutionPolicy Bypass -File setup.ps1 -SdkMsi "D:\Thales SDK x64 3.9.2.49.msi"
powershell -ExecutionPolicy Bypass -File setup.ps1 -SkipUvIrPatch
powershell -ExecutionPolicy Bypass -File setup.ps1 -LogonStart
powershell -ExecutionPolicy Bypass -File setup.ps1 -Doctor
```

`setup.ps1 -Doctor` is read-only and should be the first diagnostic step when an
installed scanner PC is not working.

### Uninstalling

[`uninstall.ps1`](uninstall.ps1) removes the bridge. It self-elevates, needs no clone
and no network, and is what `setup.ps1 -Uninstall` calls internally:

```powershell
powershell -ExecutionPolicy Bypass -File uninstall.ps1
powershell -ExecutionPolicy Bypass -File uninstall.ps1 -Port 9000   # if installed on a non-default port
powershell -ExecutionPolicy Bypass -File uninstall.ps1 -KeepLogs    # keep the SDK log folders
```

Or without a local copy:

```powershell
irm https://bitbucket.org/solaireresortcasino/thales-scanner-bridge/raw/main/uninstall.ps1 | iex
```

It removes the Scheduled Task, stops the process, drops the URL ACL, deletes
`C:\Program Files\ThalesBridge`, and deletes the working/log folders (SDK logs can
contain document data — pass `-KeepLogs` to keep them for debugging).

**It leaves the Thales SDK installed**, on purpose: it's a separate licensed product that
other apps may use, and this project never installed it. It also leaves the
`Application.ini` UV/IR patch in place — `setup.ps1` edits those values without recording
the originals, so there's nothing safe to restore. Reinstall the SDK if you need its
factory settings back.

### Auto-start: boot vs logon

By default the bridge starts **at boot, running as `SYSTEM`** — power the PC on and
`wss://localhost:8765` is serving before anyone logs in, which is what you want on an
unattended kiosk. The reader does not have to be plugged in at boot: the bridge retries
reader init every 10s until it appears.

Pass **`-LogonStart`** to instead start it at logon, as the logging-on user. Use this if
boot-start turns out not to work on your machine — the bridge itself is a plain console
app with no UI, but the Thales SDK's native DLLs talk to USB, and a driver that refuses
to enumerate the reader from a non-interactive session 0 would only show up as a bridge
that runs but never finds the reader. `setup.ps1 -Doctor` reports which trigger is
configured and whether the process and port are actually up, so check there first.

> **Verify boot-start once, on the real machine:** run `setup.ps1`, then reboot without
> logging in, and from another PC (or after logging in) confirm the port is live and a
> scan works. If `-Doctor` shows the task running as SYSTEM but the reader never
> initialises, re-run with `-LogonStart`.

On a PC without the .NET SDK and without a downloaded release, first
`dotnet publish ThalesBridge -c Release -r win-x64 --self-contained true -o publish`
on a dev machine and copy this whole repo (including the `publish` folder) over —
the script then installs the prebuilt binaries. The manual steps below remain for
reference/debugging.

## Supported hardware

**Verified against: Thales Gemalto Document Reader QS2000** — visible-light
(RGB) only, no UV/IR (confirmed against real passports; see the
`ERROR_FEATURE_NOT_SUPPORTED - "UV"` section below).

The underlying SDK (`MMMReaderDotNet50.dll`, FullPage high-level API) also
drives other Thales readers, and `ScannerService.cs`/`Contracts.cs` make no
QS2000-specific assumptions — so the bridge likely works unmodified on those
too. The one QS2000-specific piece is `setup.ps1`'s `Application.ini` patch,
which disables UV/IR because the QS2000 has no such hardware. **If your
reader does have UV/IR, pass `-SkipUvIrPatch`** or the installer will wrongly
turn off capture your hardware actually supports.

## A note on the Thales SDK itself

**This repo never includes the Thales SDK installer (`.msi`) or any of its DLLs.** They're
Thales's proprietary, licensed software (see the copyright/EULA notice in the SDK's own
documentation) — redistributing them here would violate that license. Every integrator
needs their own copy from Thales (via your hardware/SDK purchase or vendor relationship)
and points `setup.ps1 -SdkMsi` or `ThalesBridge/libs/` at their own local copy. This is
also why CI decodes the wrapper DLL from a private repo secret rather than committing it
(see [CI setup](#ci-setup-maintainers) below), and why `ThalesBridge/libs/` and any
`*.msi` are gitignored.

## Developer prerequisites (source build)

1. Install **Thales Document Reader SDK x64** (the `.msi`, obtained from Thales — not
   included in this repo). This installs the drivers and DLLs.
2. Install the **.NET 8 SDK** (x64): https://dotnet.microsoft.com/en-us/download/dotnet/8.0
   (pick "SDK x64" under Windows — the SDK, not the runtime, since we `dotnet build` on this PC).
   Skip this if you're using a downloaded `publish.zip` release instead.
3. Plug in and confirm the scanner works in the SDK's own demo app first.

## Wiring the SDK DLLs (the important part)

The bridge references **two kinds** of DLL from the SDK:

| DLL | Type | How the bridge finds it |
| --- | --- | --- |
| `MMMReaderDotNet50.dll` | managed wrapper (compile-time reference) | copy into `ThalesBridge/libs/` (the `.csproj` HintPath) |
| `MMMReaderHighLevelAPI.dll` + other native DLLs | native (runtime) | must be on the app's DLL search path at runtime |

From the SDK install directory (default `C:\Program Files\Thales\Thales Document Reader SDK x64\<version>\`):

```powershell
# 1. managed wrapper -> compile-time reference
mkdir ThalesBridge\libs
copy "C:\Program Files\Thales\...\SDK\Libraries\MMMReaderDotNet50.dll" ThalesBridge\libs\

# 2. native runtime DLLs: simplest is to run the built exe FROM the SDK Bin folder,
#    or copy the SDK Bin\*.dll next to ThalesBridge.exe after publishing.
```

If you see `DllNotFoundException` at runtime, the native DLLs aren't on the search path —
run the exe with the SDK `Bin` folder as the working directory, or copy those DLLs next to it.
(`setup.ps1` handles this for you via the generated launcher.)

## Build & run

```powershell
cd ThalesBridge
dotnet build -c Release
dotnet run -c Release                       # serves wss://localhost:8765
dotnet run -c Release -- 9000               # custom port
dotnet run -c Release -- 8765 --verbose     # + log every outgoing JSON frame
dotnet run -c Release -- 8765 --debug-log C:\temp\sdk.log   # + full SDK diagnostics (level 5)
dotnet run -c Release -- --version          # print the release version and exit
```

`--debug-log` writes the SDK's internal trace to the given file (must be a writable
location — not Program Files). Use it when scans fail with opaque errors like
`UNKNOWN_ERROR_OCCURRED`: search the log for `ERROR` lines around `FireDataCallback`.
Like `--verbose`, the log can contain document data — integration debugging only.

Console output shows reader status, completed scans, and errors. With `--verbose` it also
prints each outgoing frame's JSON (base64 images truncated) so you can track the contract
during integration. **Result frames contain patron/document PII — use `--verbose` for
integration/testing only, and never redirect it to a file in production.**

## The contract (what goes over the WebSocket)

Every frame is one JSON object — full spec in [`CONTRACT.md`](CONTRACT.md) and
[`contract/scanner-message.schema.json`](contract/scanner-message.schema.json).
`Contracts.cs` is the C# source of truth on this side; a client package
implementing it (e.g. `thales-scanner-client` for React) must state which
contract version it targets.

```jsonc
// type=status  — reader phase changed
{ "type": "status", "status": "waiting_for_document" }   // idle | waiting_for_document | reading

// type=result  — one completed scan
{
  "type": "result",
  "data": {
    "mrz": {
      "firstName": "MARIA", "middleName": null, "lastName": "SANTOS",
      "documentNumber": "P1234567", "documentType": "passport",
      "dateOfBirth": "1990-05-14", "gender": "F",
      "nationality": "PHL", "issuingCountry": "PHL", "expiryDate": "2030-05-14"
    },
    "images": { "front": "data:image/jpeg;base64,...", "back": null, "portrait": "data:image/jpeg;base64,..." },
    "chip": { "present": true, "verified": true },
    "capturedAt": "2026-07-13T09:12:00.000Z"
  }
}

// type=error — every SDK error is forwarded, message is the SDK's own text
// (e.g. code "ERROR_FEATURE_NOT_SUPPORTED", message "Feature not supported - \"UV\"")
{ "type": "error", "code": "READ_INCOMPLETE", "message": "..." }
```

Automatic capture starts when a document is placed on the glass. Both the desktop manager and Player360 capture dialog also offer **Manual** mode with a **Capture now** button. Mode changes apply to every connected client. Automatic is the default after a bridge restart; USB reconnects retain the current mode.

Manual mode disables placement-triggered reads and calls the Thales SDK's `ForceRead()` API on request. Repeated captures do not require removing the document. Controls are disabled while reading or awaiting a command acknowledgement. See [capture commands and hardware acceptance checks](CONTRACT.md#capture-controls). The manual extension is compiled and tested with simulated SDK state; Windows/scanner acceptance remains required.

## How the WebSocket is exposed

`WebSocketHub.cs` uses `System.Net.HttpListener` (built into .NET — no ASP.NET dependency):

1. Listens on `https://localhost:<port>/` (**localhost only**, never `0.0.0.0`).
2. Upgrades incoming requests via `AcceptWebSocketAsync`.
3. Keeps a thread-safe set of connected browser sockets.
4. `BroadcastAsync(...)` pushes each scanner event to every client; new clients immediately
   receive the current status frame on connect.

Because it's bound to `localhost`, only a browser running on the same PC as the scanner can
reach it — the standard front-desk / kiosk topology.

## Known issue: `ERROR_CAMERA_DRIVER_ERROR` at startup

The SDK allows **one client process at a time**. If ReaderExpo, an SDK demo app, or a
second bridge instance is running, `Reader.Initialise` fails with
`ERROR_CAMERA_DRIVER_ERROR - 536870916`. Close the other app and restart the bridge, or
run `setup.ps1 -Doctor` to confirm nothing else is holding the device.

## Known issue: `ERROR_FEATURE_NOT_SUPPORTED - "UV"` on every scan (QS2000)

The SDK ships with UV and IR image capture **enabled by default**, but the QS2000 has a
single visible-light (RGB) source only — no UV and no IR illumination. Scans fail with
`ERROR_FEATURE_NOT_SUPPORTED - "UV"` (usually followed by `UNKNOWN_ERROR_OCCURRED`) and
never emit a result. `setup.ps1` fixes this automatically; to do it by hand:

```ini
; C:\Program Files\Thales\Thales Document Reader SDK x64\<version>\Config\Application.ini
[DataToSend]
IRImage=0
UVImage=0
IRImageRear=0
UVImageRear=0
```

Restart the bridge afterwards. **Skip this on readers that actually have UV/IR hardware**
(pass `-SkipUvIrPatch` to `setup.ps1`).

## Known issue: `UNKNOWN_ERROR_OCCURRED` from a handler exception

An exception thrown inside the data callback would previously propagate into the SDK's
native reader thread and abort the whole read as an opaque `UNKNOWN_ERROR_OCCURRED`.
`OnData` catches everything and emits a `DATA_HANDLER_FAILED` error frame instead, so one
bad handler can no longer kill the scan.

## CI setup (maintainers)

[`bitbucket-pipelines.yml`](bitbucket-pipelines.yml) cross-builds the self-contained
Windows bridge and installer for branches, pull requests, and `main`. The managed
`MMMReaderDotNet50.dll` wrapper is checked in, so normal builds need no CI secret.

Each successful Bitbucket run keeps `ThalesBridgeSetup.exe`, `monitor-bridge.bat`, and
`publish.zip` as pipeline artifacts. The `qa` branch uses a version such as
`0.0.0-qa.<build-number>`. These builds do not require an Atlassian email or API token.

Pipeline artifacts are not the same as Bitbucket Downloads. They remain associated with
their pipeline run and still follow the private repository's access rules. Publishing
files separately under **Repository → Downloads** requires an authorized manual upload
or separately configured credentials; the current pipeline intentionally does not do it.

## Contributing / versioning

Commits follow [Conventional Commits](https://www.conventionalcommits.org/)
(`feat:`, `fix:`, `chore:`, etc.). GitHub `main` uses semantic-release to calculate
the next version and publish `ThalesBridgeSetup.exe` plus `publish.zip` automatically.
Bitbucket branches and semantic-version tags build pipeline artifacts, but do not
automatically create a Bitbucket Downloads release.

## ⚠️ Known limitation

- `CD_SCDG*_VALIDATE` chip validation — `ScannerService.cs` hard-codes
  `Verified = true` when a validate data item arrives; read the real
  pass/fail from the payload before trusting `chip.verified` downstream.

### Editing the endpoint

In the desktop manager, enter `wss://localhost:9000/` (or another free port) and select
**Apply endpoint**. This changes the local server, updates Windows TLS configuration,
restarts the bridge, and verifies the secure handshake before saving the new setting.
The previous launcher/settings are restored if verification fails. Remote hosts, insecure
`ws://` URLs, custom paths, credentials, query strings, and fragments are rejected.
Then change Player 360's scanner connection setting to the same URL. A browser setting
does not reconfigure the Windows bridge, and a desktop change does not update browsers.
