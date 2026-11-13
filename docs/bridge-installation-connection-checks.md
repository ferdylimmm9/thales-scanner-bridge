# Thales Bridge Installation and Connection Checks

## Installer download

[Download the supplied installer](https://drive.google.com/file/d/1Fb3t32wYRcA1Uo3P_mDvD9u47cMzEPLm/view?usp=sharing)

This link was supplied by the requester. Its file contents and version have not been verified.

## Purpose

Verify the Thales bridge installation, startup, scanner connection, and Player360 (P360) integration on a Windows scanner workstation.
Record actual results for each check. A successful WebSocket connection does not prove that a physical document scan works.

Start with bridge-to-P360 connectivity. Then verify scanner readiness and a complete scan.
If the bridge is not installed, complete sections 9 and 8 before returning to section 1.
Collect evidence when any check fails. Test startup and reboot behavior after the main scanning checks.

## Release status

Use an approved bridge build that includes the required features.
Record its version, source branch, pipeline run, and file hash.
Automatic reconnection and saved diagnostic logs require a build that includes those changes.

## 1. Check bridge-to-P360 connectivity

1. Open Scanner Manager on the workstation connected to the scanner.
2. Copy the URL from its **Bridge endpoint** field into the test results.
3. Include the complete protocol, hostname, and port, such as `wss://localhost:8765/` or `ws://localhost:8765/`.
4. Open P360 on the same workstation.
5. Open its scanner connection settings.
6. Save the exact URL copied from Scanner Manager.
7. Keep the scanner settings panel open while checking its connection.
8. Confirm that P360 reports a connected scanner bridge.
9. Confirm that Scanner Manager shows **Bridge: connected** on the same port.
10. Confirm that its log records a successful WebSocket handshake.
11. For WSS, confirm that the localhost certificate is valid and trusted.
12. If connection fails, run **Diagnostics** and collect the error before continuing.

**Expected result:** P360 and Scanner Manager connect to the same local bridge endpoint.
A successful handshake does not prove that a physical scan works.

## 2. Check scanner readiness

1. Confirm that Scanner Manager shows **Scanner: connected and ready**.
2. Confirm that the reader phase is `waiting_for_document`.
3. Confirm that no unresolved reader initialization error remains.
4. A connected bridge with an unavailable reader fails this check.

## 3. Verify a scan reaches P360

1. Open the document capture dialog.
2. Scan a test document.
3. Confirm that the current document image appears in P360.
4. Confirm that the selected image reaches the intended form.
5. For passports, verify available MRZ fields against the test document.
6. For documents without MRZ, confirm that an available document image is accepted.
7. Do not fail an image-only ID scan because MRZ fields are empty.
8. Confirm that manual upload remains available when the scanner is unavailable.

## 4. Check repeated automatic scans

1. Select Automatic mode.
2. Place an approved test document on the reader.
3. Confirm that the reader reports reading.
4. Confirm that completed scans increases.
5. Confirm that the reader returns to `waiting_for_document`.
6. Remove and reposition the document before repeating the automatic scan.
7. Repeat three times.
8. Confirm that each attempt produces one fresh result.

## 5. Check manual capture

1. Select Manual mode in Scanner Manager.
2. Place the test document on the reader.
3. Confirm that placement alone does not start a scan.
4. Click **Capture now**.
5. Confirm that completed scans increases and the reader returns to ready.
6. Repeat capture without removing the document.
7. Confirm that each capture produces a fresh result.
8. Repeat the manual capture check from P360.
9. Confirm that both screens show the same confirmed capture mode.
10. A command acknowledgement confirms command acceptance, not scan completion.

## 6. Check endpoint changes and recovery

1. Change the bridge to an unused localhost endpoint.
2. Confirm that the bridge restarts and Scanner Manager reconnects automatically.
3. Update P360 to the same endpoint.
4. Confirm that P360 reconnects and completes another scan.
5. Attempt a different port already occupied by an approved test listener.
6. Confirm that the change fails with an endpoint conflict message.
7. Confirm that the previous endpoint and settings remain usable.
8. Disconnect and reconnect scanner USB.
9. Confirm that reader availability recovers.
10. Stop and start the bridge from Scanner Manager.
11. Confirm that the connection recovers.
12. For WSS failures, check certificate trust, expiry, and hostname.
13. For WS failures, check browser policy and protocol agreement.
14. Check P360 browser errors for blocked localhost access or CSP failures.
15. Do not disable browser protections to pass the test.

## 7. Check automatic startup after reboot

1. Open Windows Task Scheduler.
2. Find the ThalesBridge task.
3. Confirm that the task is enabled.
4. For boot startup, confirm an At startup trigger and the SYSTEM account.
5. For logon startup, confirm the intended trigger and account.
6. Restart the workstation.
7. Confirm that the bridge starts without opening Scanner Manager manually.
8. Confirm boot-before-login behavior separately when that behavior is required.
9. Open Scanner Manager after login.
10. Confirm that closing Scanner Manager leaves the bridge running.

## 8. Check installation when setup or repair is required

1. Run the approved installer.
2. Select the required startup behavior.
3. Confirm that installation completes without an error.
4. Confirm that `C:\Program Files\ThalesBridge` contains `ThalesBridge.exe`, `ThalesBridgeManager.exe`, `setup.ps1`, and `bridge-settings.json`.
5. Open Thales Scanner Bridge from its shortcut.
6. Copy the complete **Bridge endpoint** URL from Scanner Manager into the test results.
7. Confirm that the endpoint uses localhost and the required port.
8. Confirm that only one bridge process controls the reader.

## 9. Check workstation and SDK prerequisites

1. Confirm that the workstation runs supported Windows x64.
2. Confirm that the licensed Thales Document Reader SDK x64 is installed.
3. Connect scanner power and USB.
4. Confirm that Windows detects the reader without a device error.
5. Close ReaderExpo and other scanner applications.
6. Use an administrator account for installation and endpoint configuration.

## 10. Collect diagnostic evidence

1. Record the date, workstation identifier, scanner model, SDK version, bridge version, and P360 environment.
2. Record the endpoint, startup mode, expected result, and actual result.
3. Save the Scanner Manager report.
4. For builds with saved logs, use **Open logs**.
5. The manager log folder is `%LOCALAPPDATA%\ThalesBridge\Logs`.
6. Include error codes, timestamps, connection attempts, and conflict PID/process details when available.
7. Remove patron information and document images from shared reports.
8. Do not attach raw scan payloads or SDK logs without reviewing their contents.

## Acceptance criteria

- [ ] P360 connects to the same bridge endpoint shown in Scanner Manager.
- [ ] Scanner Manager confirms reader readiness.
- [ ] A physical scan reaches P360 and the intended form.
- [ ] Repeated automatic and manual capture pass.
- [ ] Image-only IDs are accepted when a document image is available.
- [ ] Available passport MRZ data remains usable.
- [ ] Endpoint changes reconnect successfully.
- [ ] Occupied endpoints produce useful errors without replacing working settings.
- [ ] USB and bridge restart recovery succeed.
- [ ] The selected automatic startup behavior works after restart.
- [ ] Installation completes without unresolved errors.
- [ ] Logs identify the failed stage when a check fails.
- [ ] All checks have recorded actual results.

## Evidence limits

These are acceptance procedures, not completed workstation test results.
Record unsupported hardware behavior and unresolved failures before approving deployment.
