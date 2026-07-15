# thales-scanner-bridge

WebSocket bridge for the **Thales Gemalto Document Reader QS2000** — turns
MRZ / ePassport / ID scans into JSON over `ws://localhost` for any web app.
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
[Thales QS2000] --USB--> [ThalesBridge.exe]  --ws://localhost:8765-->  [your web app]
   driver               wraps MMMReaderDotNet50.dll,                   any WebSocket client
                        emits DocumentScanResult JSON                   (see CONTRACT.md)
```

> **This project only runs on the Windows PC where the scanner is plugged in** — the SDK's
> native DLLs and the reader hardware are Windows-only. It can be *compile-checked* on
> macOS/Linux with `dotnet build -c Release -p:EnableWindowsTargeting=true` (given
> `MMMReaderDotNet50.dll` in `libs/`); real builds run on `windows-latest` in CI.

## What is mandatory before you install

Read this first — the installer **cannot** work around any of it.

| # | Mandatory | Why |
|---|---|---|
| 1 | **Windows** PC, x64 | The SDK's native DLLs and the reader driver are Windows-only. There is no macOS/Linux runtime. |
| 2 | **Thales Document Reader SDK x64** — the `.msi`, e.g. `Thales Document Reader SDK x64 3.9.2.49.msi` | Installs the USB drivers **and** the native DLLs the bridge calls at run time. **`setup.ps1` stops at step 1/7 without it** — this is a hard gate, not a warning. |
| 3 | The **reader plugged in** (QS2000 or another FullPage-API model) | Nothing to scan otherwise. The bridge itself starts fine without it and retries every 10s until it appears, so you can install first and plug in after. |
| 4 | An **elevated (Administrator)** PowerShell | Needed to install the MSI, write to `C:\Program Files`, add the URL ACL, and register the Scheduled Task. |

> ### ⚠️ The SDK `.msi` is not in this repo, and never will be
>
> It is Thales's licensed, proprietary software — redistributing it here would
> breach that licence. **You must obtain your own copy** via your QS2000/reader
> purchase, your Thales sales contact, or whoever owns that hardware
> relationship at your org. See
> [A note on the Thales SDK itself](#a-note-on-the-thales-sdk-itself).
>
> The `MMMReaderDotNet50.dll` checked into `ThalesBridge/libs/` is only the
> *managed wrapper*. It lets the project **compile** without the SDK — it does
> **not** let it **run**. Compiling and running are different things.

Not mandatory: the **.NET 8 SDK**. You only need it to build from source; the
released `publish.zip` is self-contained. See
[Prerequisites](#prerequisites-on-the-scanner-pc) for the full list.

## Fastest path: kiosk PC, no dev tools, no clone

One line in an elevated PowerShell — downloads the latest release and installs it.
**Point it at your SDK `.msi`** (step 2 above) unless the SDK is already installed
on this PC:

```powershell
# have the installer install the SDK for you (silently) as part of the run:
$env:THALES_SDK_MSI = "C:\path\to\Thales Document Reader SDK x64 3.9.2.49.msi"
irm https://raw.githubusercontent.com/ferdylimmm9/thales-scanner-bridge/main/install.ps1 | iex
```

```powershell
# or, if you already installed the SDK by hand:
irm https://raw.githubusercontent.com/ferdylimmm9/thales-scanner-bridge/main/install.ps1 | iex
```

If you run it with no SDK installed and no `THALES_SDK_MSI` set, it stops with
`FAILED: Thales SDK not installed` and tells you exactly this. That is the gate
doing its job, not a bug.

(Review [`install.ps1`](install.ps1) first if you'd rather not pipe a remote script blind —
it only talks to `github.com` and `localhost`, same pattern as rustup/deno's installers.)
Something wrong later? Re-run `setup.ps1 -Doctor` from the folder it installed to
(printed at the end) — read-only diagnostic report, no reinstall, the first thing to
check before anything else.

**Prefer to download manually instead:**

1. Download `publish.zip` from the [latest release](../../releases/latest) and extract
   it next to `setup.ps1` as a folder named `publish`.
2. Elevated PowerShell: `powershell -ExecutionPolicy Bypass -File setup.ps1`

`setup.ps1` automates the whole per-machine rollout — SDK check (or silent MSI
install), the QS2000 UV/IR config patch, publish + install to
`C:\Program Files\ThalesBridge`, URL ACL for non-admin users, a start-at-boot
Scheduled Task, and a listening-port verification:

```powershell
# elevated PowerShell, from this repo's root:
powershell -ExecutionPolicy Bypass -File setup.ps1
powershell -ExecutionPolicy Bypass -File setup.ps1 -SdkMsi "D:\Thales SDK x64 3.9.2.49.msi"
powershell -ExecutionPolicy Bypass -File setup.ps1 -SkipUvIrPatch   # reader has UV/IR hardware (not QS2000)
powershell -ExecutionPolicy Bypass -File setup.ps1 -LogonStart      # start at logon instead of at boot
powershell -ExecutionPolicy Bypass -File setup.ps1 -Doctor          # read-only diagnostic, no changes
powershell -ExecutionPolicy Bypass -File setup.ps1 -Uninstall
```

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
irm https://raw.githubusercontent.com/ferdylimmm9/thales-scanner-bridge/main/uninstall.ps1 | iex
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
`ws://localhost:8765` is serving before anyone logs in, which is what you want on an
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

## Prerequisites (on the scanner PC)

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
dotnet run -c Release                       # serves ws://localhost:8765
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

The client sends **no** commands in v1 — the reader auto-triggers when a document is
placed on the glass.

## How the WebSocket is exposed

`WebSocketHub.cs` uses `System.Net.HttpListener` (built into .NET — no ASP.NET dependency):

1. Listens on `http://localhost:<port>/` (**localhost only**, never `0.0.0.0`).
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

`build.yml` compiles against the **real** SDK wrapper, not a stub, so it actually catches
SDK API drift. Since `MMMReaderDotNet50.dll` is proprietary and gitignored, CI needs it via
repo secrets instead. GitHub secrets cap out at 64 KB; the DLL's raw base64 is ~215 KB and
even gzip'd base64 is ~85 KB, so it's gzip'd, base64'd, then **split across two secrets**,
`MMM_READER_DOTNET_DLL_B64_1` and `_2`:

```bash
# from the SDK's Libraries folder, any shell with gzip/base64/split (macOS/Linux/WSL/Git Bash):
gzip -9 -c MMMReaderDotNet50.dll | base64 > dll.b64
split -n 2 dll.b64 part_   # -> part_aa, part_ab
```

— then set `part_aa`'s contents as `MMM_READER_DOTNET_DLL_B64_1` and `part_ab`'s as
`MMM_READER_DOTNET_DLL_B64_2` under Settings → Secrets and variables → Actions (or
`gh secret set MMM_READER_DOTNET_DLL_B64_1 < part_aa`, etc., if you have the `gh` CLI).
CI concatenates the two, base64-decodes, and gunzips back to the original DLL. GitHub
doesn't expose repo secrets to workflows triggered by forked-repo PRs, so this only builds
for same-repo branches/PRs; external contributors' PRs won't compile in CI (a known,
accepted gap, not a bug).

## Contributing / versioning

Commits follow [Conventional Commits](https://www.conventionalcommits.org/)
(`feat:`, `fix:`, `chore:`, etc.) — releases are cut automatically by
`semantic-release` on every push to `main` (see `.github/workflows/release.yml`),
including a `publish.zip` attached to each GitHub Release for the no-.NET-SDK
install path above.

## ⚠️ Known limitation

- `CD_SCDG*_VALIDATE` chip validation — `ScannerService.cs` hard-codes
  `Verified = true` when a validate data item arrives; read the real
  pass/fail from the payload before trusting `chip.verified` downstream.
