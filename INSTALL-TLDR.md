# Installation Guide — Thales Scanner Bridge

Use this guide on the **Windows x64 PC connected to the Thales reader**.

## Files to prepare

1. `ThalesBridgeSetup.exe`
2. The licensed **Thales Document Reader SDK x64** `.msi`, unless it is already installed
3. `monitor-bridge.bat` for optional post-install monitoring

An authorized team member downloads the files from a successful Bitbucket pipeline run
for the `qa` branch. Because the repository is private, users without repository access
must receive the files through an approved internal file share or removable drive.
`ThalesBridgeSetup.exe` works offline after it has been copied to the scanner PC.

The Thales SDK is licensed software and is not included in the bridge installer.

## Install

1. Connect the Thales reader to power and USB.
2. Double-click `ThalesBridgeSetup.exe`.
3. Approve the Windows Administrator prompt.
4. If the setup window says the Thales SDK is not detected, click **Browse** and select
   the licensed SDK `.msi`.
5. Keep WebSocket port **8765**.
6. For a QS2000, keep **Disable UV/IR capture** selected.
7. Click **Install** and wait for **Installed successfully**.

The client does not need to open a command line or install the .NET SDK.

## Verify

In the setup window, confirm:

1. `Bridge: connected on port 8765`
2. `Scanner: connected and ready`
3. Place a document on the reader.
4. Confirm `Scanner: reading document`.
5. Confirm `Activity: scan completed`.

After closing setup, double-click `monitor-bridge.bat` to monitor connection and scan
activity. It does not print document names, numbers, or images.

## If the scanner is not detected

1. Check scanner power and USB.
2. Close ReaderExpo or any Thales SDK demo application.
3. Close any second instance of the bridge.
4. Restart the PC and run `monitor-bridge.bat` again.

The SDK permits only one application to control the reader at a time.

## Installed behavior

- Installation directory: `C:\Program Files\ThalesBridge`
- Local WebSocket: `wss://localhost:8765`
- Default startup: at Windows boot as `SYSTEM`
- The bridge retries automatically when the reader is disconnected


### Updated secure installer

Reinstall with the new `ThalesBridgeSetup.exe` on every scanner PC, then set the
frontend URL to `wss://localhost:8765/`. Setup creates/trusts the local TLS certificate.
Open **Thales Scanner Bridge** from the desktop for connection details, Thales status,
Start/Stop/Restart, Enable auto-start, and Diagnostics. Closing the window leaves the
bridge running. The default startup task starts at Windows boot and retries on failure.
The manager opens automatically after installation. Use Diagnostics to check certificate
expiry and rerun setup before it expires. Verify a reboot and repeated scans on the actual PC.
