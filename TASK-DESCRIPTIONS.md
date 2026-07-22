# Jira Task Descriptions — Thales Scanner Bridge

Ticket descriptions for the recent work on this repo. Format: Description + Acceptance Criteria.

---

## TSB-1 — Run the bridge at boot as SYSTEM + add a standalone uninstaller

**Description**

Register the Scheduled Task with a **boot trigger running as SYSTEM** (instead of `/sc onlogon`), so the bridge serves `ws://localhost` from power-on with no interactive logon — required for unattended kiosk PCs. `-LogonStart` preserves the old onlogon behaviour as a fallback. `-Doctor` now reports the actual trigger rather than only asserting the task exists.

Add `uninstall.ps1`: self-elevating and self-contained — removes the Scheduled Task, running process, URL ACL, install dir, and working/log folders. `setup.ps1 -Uninstall` forwards to it; `install.ps1` fetches it alongside `setup.ps1`. The Thales SDK and the `Application.ini` UV/IR patch are intentionally left untouched.

**Acceptance Criteria**

- [ ] Task runs as SYSTEM on a boot trigger; bridge listens on `ws://localhost:8765` after reboot with no logon.
- [ ] `-LogonStart` restores onlogon behaviour.
- [ ] `-Doctor` reports the actual configured trigger.
- [ ] `uninstall.ps1` self-elevates and removes task, process, URL ACL, install dir, and log folders.
- [ ] `setup.ps1 -Uninstall` and `install.ps1` both wire up the uninstaller.
- [ ] SDK install and UV/IR patch are not removed/modified.
- [ ] **Open risk:** verify SDK native DLLs enumerate USB from session 0. If not, symptom is bridge running + port listening + reader never initialising; workaround is `-LogonStart`.

---

## TSB-2 — Fix four Windows PowerShell 5.1 install defects ported from player360_v2

**Description**

This repo was extracted from `player360_v2/bridge` **before** the fixes in `player360_v2@1c469fb1` landed, so four already-diagnosed install defects were never carried over — and v1.2.0 shipped with all four. Root cause across the board: nothing in CI ever exercised the installers.

Defects fixed:

1. **Encoding** — no UTF-8 BOM + em dashes caused PS 5.1 to decode as CP1252, so `setup.ps1` failed to parse at all. Saved all three scripts with a BOM. *(Note: partially reverted in TSB-4 — see that ticket.)*
2. **schtasks/netsh stderr** under `ErrorActionPreference='Stop'` aborted a clean install at step 6. Routed through `cmd`.
3. **schtasks `/tr` quote loss** registered `Command=C:\Program` and failed with `0x80070002`. Re-registered via the `ScheduledTasks` cmdlets.
4. **Battery defaults** left the task Queued on laptops/tablets. Overridden, and the 3-day execution limit lifted.

Adds a `scripts` CI job (parse under PS 5.1) and an `install-smoke` job that runs the documented `cmd.exe` install commands on real Windows.

**Acceptance Criteria**

- [ ] `setup.ps1` parses and runs end-to-end under Windows PowerShell 5.1.
- [ ] Clean install completes past step 6 without stderr aborting it.
- [ ] Scheduled Task registers with the correct quoted command path (no `0x80070002`).
- [ ] Task runs on battery / laptop / tablet (not left Queued); no 3-day execution cap.
- [ ] CI `scripts` job validates parsing; `install-smoke` job runs documented install commands on real Windows.

*Credit: Anna Kurniaty for the original diagnosis and fixes.*

---

## TSB-3 — Surface mandatory requirements above the install one-liner (docs)

**Description**

Docs-only. The SDK `.msi` requirement was buried ~100 lines down in Prerequisites, below the install one-liner. Move a **mandatory-requirements table** (Windows x64, the SDK `.msi` hard gate, the reader, an elevated shell) above the one-liner. Add an explicit note that the `.msi` is licensed and never bundled, and clarify that the checked-in `MMMReaderDotNet50.dll` is only the **managed wrapper** — it makes the project *compile* without the SDK, not *run* without it.

**Acceptance Criteria**

- [ ] Mandatory-requirements table appears above the install one-liner in README.
- [ ] SDK `.msi` documented as licensed/not-bundled with the hard-gate behaviour called out.
- [ ] `MMMReaderDotNet50.dll` clarified as compile-only managed wrapper.
- [ ] No code changes.

---

## TSB-4 — Fix `irm | iex` broken by the BOM; make scripts ASCII-only

**Description**

v1.2.1 added a UTF-8 BOM (TSB-2, defect 1) to fix `powershell -File` under 5.1 — but that broke the documented `irm ... | iex` one-liner: `irm` decodes the BOM into a leading `U+FEFF`, so `iex` never recognises the opening `<#` and parses the comment header as code, failing with `Missing opening '(' after keyword 'for'`.

The two invocation paths have **opposite** requirements: `-File` reads a file and needs a BOM for non-ASCII; `irm | iex` reads a string and cannot have one. The only configuration satisfying both is **ASCII-only, no BOM** (CP1252 and UTF-8 agree below `0x80`). The sole offending character was the em dash — removed. The `scripts` CI job previously *required* the BOM, so it was enforcing the bug; it now asserts ASCII-only, no BOM, and parses both ways (`ParseFile` and `ParseInput`).

**Acceptance Criteria**

- [ ] All scripts are ASCII-only with no BOM.
- [ ] `irm ... | iex` one-liner runs cleanly.
- [ ] `powershell -File setup.ps1` still runs under Windows PowerShell 5.1.
- [ ] CI asserts ASCII-only + no BOM and validates both `ParseFile` and `ParseInput`.
