# Inno Setup installer

The release build now targets Inno Setup 7.1.1. The legacy WinForms setup source is
retained for reference, but release scripts no longer wrap its executable.
The desktop Scanner Manager remains a .NET application.

## Installation and upgrades

- Use the single `ThalesBridgeSetup.exe` wizard with administrator approval.
- Provide the licensed Thales SDK MSI if the SDK is not already installed.
- The installer removes the legacy startup task and leaves the bridge paused.
- Click **Run** in Scanner Manager to launch the bridge without a console window.
- Click **Pause** or close the manager to shut down the bridge and release the reader.
- Change the endpoint while paused, or apply a change while running to restart the bridge.
- The manager captures the bridge process output in its log.
- Scanner Manager opens from its desktop/Start menu shortcut. It requests elevation for management operations.
- Existing endpoint settings are retained. New installations default to `wss://localhost:8765/`.
- Edit the endpoint afterward through Scanner Manager; use the same endpoint in P360.
- Run a newer installer over an existing installation to upgrade. Rerun the same release to replace/repair files.
- Windows Installed apps provides Uninstall. Rerunning the installer also offers an existing-uninstaller button.
- Uninstall stops the bridge and removes its task, owned files, shortcuts, and owned endpoint/TLS configuration.
- The licensed SDK and diagnostic logs are retained.

The application ID and install directory are fixed across versions. Existing legacy
installations are detected through their task and settings during migration. The new
installer does not install a second copy into a version-specific directory.

## Build on Windows (recommended)

Install the .NET 8 SDK. Then run from the repository root:

```powershell
$compiler = ./packaging/install-compiler.ps1
./build-release.ps1 -Version 1.3.0 -Compiler $compiler
```

The compiler bootstrap uses an official immutable Inno 7.1.1 release URL, verifies
its SHA-256 digest, verifies its Authenticode signature and publisher, and stops on
failure. Updating the compiler requires reviewing and updating the pinned digest.
GitHub build/release workflows use Windows hosted runners and this bootstrap.

Bridge and manager are published into one directory with shared runtime files.
Inno compresses these files directly using solid LZMA2. There is no nested custom
setup executable or duplicate manager runtime bundle. Runtime files remain included
so the workstation does not need a separate .NET download. The SDK is not bundled.

## Linux build compatibility

Bitbucket's Linux build uses Wine, Xvfb, and the same pinned official compiler.
`packaging/install-compiler.sh` verifies the release SHA-256 digest before running it.
This is a compatibility build path; native Windows CI is the recommended compiler check.
A local macOS build needs a supported Wine installation. Do not bypass Gatekeeper
or antivirus protections to make this work.

## Logging and silent deployment

Setup logging is always enabled. Use `/LOG="C:\Support\ThalesSetup.log"` to select
an explicit log path. The parent directory must exist.
Bridge configuration transcripts are under `C:\ProgramData\ThalesBridge\InstallerLogs`.
Scanner Manager logs are under `%LOCALAPPDATA%\ThalesBridge\Logs`.
These logs have different owners and serve different diagnostic purposes.

Example for an already installed SDK:

```text
ThalesBridgeSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="C:\Support\ThalesSetup.log"
```

`/SDKMSI="C:\Approved\Thales SDK.msi"` can supply the licensed SDK.
Installation does not start the bridge. After sign-in, open Scanner Manager and click **Run**.

## Signing and security

The Inno compiler's signature is not the signature of our application. Production
setup/uninstall signing requires the organization's code-signing service or certificate.
The `.iss` script accepts a `SigningTool` preprocessor value and enables signed
uninstallers when it is supplied. Configure the matching Inno sign tool on the build
machine. Never commit signing credentials. Unsigned local previews are not production
signed releases.

All installer helper executables use absolute system paths. No unverified network
payload is downloaded by the installed application. Prerequisites are checked before
the bridge is stopped. An aborted preparation attempts to restore the prior task state.
A configuration failure stops setup and records an error; full transactional rollback
of SDK/configuration side effects is not promised. Rerun setup to repair a partial installation.

## Validation status

PowerShell parse checks and mocked startup, endpoint-switch, rollback and uninstall
settings checks run in CI. The Windows workflow compiles the actual `.iss` script.
Before release, run the workstation acceptance checklist in
`bridge-installation-connection-checks.md`, including fresh install, legacy upgrade,
same-version repair, no automatic startup after reboot, Run/Pause/close, USB recovery, and uninstall.

A new Inno executable has not yet been compiled or run on the local macOS host.
The existing local installer executable is the earlier custom preview until a
successful Inno compiler run replaces it. Validate the generated Inno artifact in CI
and on a Windows scanner workstation before distribution.
