# Install Thales Scanner Bridge

Use the approved **Inno Setup** `ThalesBridgeSetup.exe` build on a Windows x64 PC
connected to the scanner. The licensed Thales SDK is separate and is not bundled.
The local custom-preview EXE predating the migration is not the Inno installer.

1. Connect scanner power and USB. Close ReaderExpo and other scanner applications.
2. Open `ThalesBridgeSetup.exe` and approve administrator access.
3. Select the licensed SDK MSI if the SDK is not installed.
4. Select **Disable UV/IR** for a QS2000. Check the reader requirements for other models.
5. Complete the wizard. Confirm that Windows Installed apps lists **Thales Scanner Bridge**.
6. Confirm that the bridge stays paused.
7. Open Scanner Manager from the desktop or Start menu shortcut. Click **Run**.
8. Confirm **Bridge: connected** and **Scanner: connected and ready**.
9. Save the same endpoint in P360's scanner settings.
10. Complete a physical scan and verify its image reaches P360.
11. Restart Windows and confirm that the bridge stays stopped until you open Scanner Manager and click **Run**.

New installations use `wss://localhost:8765/`. Upgrades retain saved endpoint settings.
Use Scanner Manager to change protocol or port, then update P360 to match.
WSS requires a trusted localhost certificate. WS does not use TLS and can be restricted
by the browser or site policy.

The Inno installer removes the legacy bridge startup task. **Pause** and closing
Scanner Manager stop the bridge so another application can use the reader.
The bridge runs without a Command Prompt window. Its output appears in Scanner Manager.

For `ERROR_CAMERA_DRIVER_ERROR` or `ERROR_NOT_INITIALISED`, pause the bridge.
Close other scanner applications. Unplug and reconnect USB, then wait for Windows to detect the reader.
Click **Run**. Confirm reader readiness and scan a test document in P360.
If the error returns, save a report before reinstalling. USB recovery does not identify the cause.

Run a newer installer over the existing installation to upgrade. Rerun the same build
to replace/repair files. Do not manually uninstall before an ordinary upgrade.
Use Windows **Installed apps → Thales Scanner Bridge → Uninstall** to remove it.
The installer also offers an **Uninstall existing** button for Inno installations.
The licensed SDK and diagnostic logs are retained.

Use **Diagnostics**, **Open logs**, and **Save report** in Scanner Manager when a check
fails. Installer configuration logs are in `C:\ProgramData\ThalesBridge\InstallerLogs`.
A connected bridge alone does not prove that scanner hardware works.

See [the full acceptance checklist](docs/bridge-installation-connection-checks.md)
and [installer build details](docs/inno-installer.md).
