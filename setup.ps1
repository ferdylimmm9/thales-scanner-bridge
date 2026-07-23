<#
.SYNOPSIS
  One-shot installer for the Thales scanner bridge on a kiosk / scanner PC.

.DESCRIPTION
  Run AS ADMINISTRATOR. Performs the whole per-machine rollout checklist:
    1. Verifies the Thales Document Reader SDK is installed (or installs it
       silently when -SdkMsi is given).
    2. Patches the SDK's Application.ini: disables UV/IR capture. This is
       specific to the QS2000 (visible-light only) - pass -SkipUvIrPatch on
       any other Thales FullPage-API reader that actually has UV/IR hardware,
       or this step would wrongly turn working capture off.
    3. Builds/publishes the bridge (or uses a prebuilt 'publish' folder next
       to this script, e.g. downloaded from Bitbucket Downloads) and copies it
       to C:\Program Files\ThalesBridge.
    4. Writes a launcher that puts the SDK Bin folder on PATH and uses a
       writable working directory.
    5. Registers the URL ACL so a non-admin kiosk user may bind the port.
    6. Creates a Scheduled Task that starts the bridge at boot, as SYSTEM, so
       the reader serves ws://localhost without anyone logging in. Pass
       -LogonStart for the old behaviour (start at logon, as the logging-on
       user) if the SDK turns out not to drive the reader from session 0.
    7. Starts the bridge now and verifies the WebSocket port is listening.

  Pass -Doctor to skip all of the above and instead run a read-only
  diagnostic report against whatever is already installed - use this first
  when troubleshooting a kiosk that "isn't working", before re-running the
  full install.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File setup.ps1
  powershell -ExecutionPolicy Bypass -File setup.ps1 -SdkMsi "D:\Thales SDK x64 3.9.2.49.msi" -Port 8765
  powershell -ExecutionPolicy Bypass -File setup.ps1 -SkipUvIrPatch
  powershell -ExecutionPolicy Bypass -File setup.ps1 -LogonStart
  powershell -ExecutionPolicy Bypass -File setup.ps1 -Doctor
  powershell -ExecutionPolicy Bypass -File setup.ps1 -Uninstall
#>
[CmdletBinding()]
param(
  [string]$SdkMsi = '',
  [int]$Port = 8765,
  [switch]$SkipUvIrPatch,
  [switch]$LogonStart,
  [switch]$Doctor,
  [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$InstallDir = 'C:\Program Files\ThalesBridge'
$TaskName = 'ThalesBridge'

function Fail($msg) { Write-Host "FAILED: $msg" -ForegroundColor Red; exit 1 }
function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }
function Pass($msg) { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Err($msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red }

# ---- admin check -----------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Fail 'run this script from an elevated (Administrator) PowerShell.' }

# ---- doctor: read-only diagnostic report -----------------------------------
if ($Doctor) {
  Write-Host "Thales scanner bridge - diagnostic report`n" -ForegroundColor Cyan
  $failures = 0

  # 1. SDK present?
  $sdkRoot = 'C:\Program Files\Thales\Thales Document Reader SDK x64'
  if (Test-Path $sdkRoot) {
    $sdkVersionDir = Get-ChildItem $sdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    Pass "SDK installed: $($sdkVersionDir.Name)"
    $appIni = Join-Path $sdkVersionDir.FullName 'Config\Application.ini'
    if (Test-Path $appIni) {
      $ini = Get-Content $appIni -Raw
      if ($ini -match '(?m)^UVImage=0' -and $ini -match '(?m)^IRImage=0') {
        Pass "Application.ini: UV/IR capture disabled"
      } else {
        Warn "Application.ini: UV/IR capture still enabled - re-run setup.ps1 (unless your reader has UV/IR)"
      }
    } else { Err "Application.ini not found under $($sdkVersionDir.FullName)"; $failures++ }
  } else { Err "Thales SDK not found at $sdkRoot"; $failures++ }

  # 2. Bridge installed?
  $exePath = Join-Path $InstallDir 'ThalesBridge.exe'
  if (Test-Path $exePath) {
    $ver = & $exePath --version 2>$null
    Pass "Bridge installed: $InstallDir (version $ver)"
  } else { Err "Bridge not found at $exePath"; $failures++ }

  # 3. Scheduled task - existence is not enough: check what actually triggers it,
  #    and (for logon triggers) whether it is scoped to one user rather than any.
  # Via cmd: schtasks writes to stderr when the task is absent, and 5.1 turns
  # native stderr into a NativeCommandError that 'Stop' makes fatal.
  $task = cmd /c "schtasks /query /tn $TaskName /fo LIST 2>nul"
  if ($LASTEXITCODE -eq 0) {
    $status = ($task | Select-String '^Status:\s*(.+)$').Matches.Groups[1].Value
    Pass "Scheduled task '$TaskName' exists (status: $status)"

    $xml = (cmd /c "schtasks /query /tn $TaskName /xml ONE 2>nul") -join "`n"
    if ([string]::IsNullOrWhiteSpace($xml)) {
      Warn "Could not read the task definition XML - skipping trigger check"
    } elseif ($xml -match '<BootTrigger>') {
      Pass "Trigger: at boot - bridge starts without anyone logging in"
    } elseif ($xml -match '<LogonTrigger>') {
      if ($xml -match '(?s)<LogonTrigger>.*?<UserId>(.*?)</UserId>.*?</LogonTrigger>') {
        Warn "Trigger: at logon, but ONLY for user '$($Matches[1])' - no other account starts the bridge. Re-run setup.ps1 (no -LogonStart) to start it at boot instead."
      } else {
        Warn "Trigger: at logon (any user) - nothing runs until someone logs in. Re-run setup.ps1 (no -LogonStart) to start it at boot instead."
      }
    } else { Err "Task has no boot or logon trigger - it will not auto-start"; $failures++ }

    if ($xml -match '<UserId>S-1-5-18</UserId>' -or $xml -match 'NT AUTHORITY\\SYSTEM') {
      Pass "Runs as: SYSTEM"
    }
  } else { Err "Scheduled task '$TaskName' not found"; $failures++ }

  # 4. Process running?
  $proc = Get-Process ThalesBridge -ErrorAction SilentlyContinue
  if ($proc) { Pass "ThalesBridge.exe is running (PID $($proc.Id))" }
  else { Warn "ThalesBridge.exe is not currently running" }

  # 5. Port listening?
  if (Test-NetConnection -ComputerName localhost -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) {
    Pass "Port $Port is listening (ws://localhost:$Port)"
  } else { Err "Nothing is listening on port $Port"; $failures++ }

  # 6. URL ACL?
  $acl = (cmd /c "netsh http show urlacl url=http://localhost:$Port/ 2>nul") -join "`n"
  if ($acl -match 'BUILTIN\\Users') { Pass "URL ACL grants BUILTIN\Users access to port $Port" }
  else { Warn "No URL ACL found for port $Port - non-admin users may not be able to (re)start the bridge" }

  Write-Host ""
  if ($failures -eq 0) {
    Write-Host "All checks passed." -ForegroundColor Green
    exit 0
  } else {
    Write-Host "$failures check(s) failed. Re-run 'setup.ps1' (elevated) to fix, or see README.md troubleshooting." -ForegroundColor Red
    exit 1
  }
}

# ---- uninstall -------------------------------------------------------------
# uninstall.ps1 is the canonical implementation (and works standalone); this
# switch just forwards to it so the two can't drift apart. The inline fallback
# covers a setup.ps1 downloaded on its own, without the rest of the repo.
if ($Uninstall) {
  $uninstaller = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'uninstall.ps1'
  if (Test-Path $uninstaller) {
    & $uninstaller -Port $Port
    exit $LASTEXITCODE
  }
  Step "Uninstalling"
  # Via cmd: these write to stderr when the task/reservation is already absent,
  # and 5.1 turns native stderr into a NativeCommandError that 'Stop' makes fatal.
  cmd /c "schtasks /end /tn $TaskName >nul 2>&1"
  cmd /c "schtasks /delete /tn $TaskName /f >nul 2>&1"
  Get-Process ThalesBridge -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
  cmd /c "netsh http delete urlacl url=http://localhost:$Port/ >nul 2>&1"
  if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
  Write-Host "Uninstalled. (SDK and Application.ini were left untouched.)" -ForegroundColor Green
  Write-Host "Note: log folders under %LOCALAPPDATA%\ThalesBridge were kept - uninstall.ps1 removes those too."
  exit 0
}

# ---- 1. Thales SDK ---------------------------------------------------------
Step "1/7 Thales Document Reader SDK"
$sdkRoot = 'C:\Program Files\Thales\Thales Document Reader SDK x64'
if (-not (Test-Path $sdkRoot) -and $SdkMsi) {
  if (-not (Test-Path $SdkMsi)) { Fail "SDK MSI not found: $SdkMsi" }
  Write-Host "  installing SDK silently from $SdkMsi (takes a few minutes)..."
  $p = Start-Process msiexec -ArgumentList "/i `"$SdkMsi`" /qn /norestart" -Wait -PassThru
  if ($p.ExitCode -ne 0) { Fail "msiexec exited with $($p.ExitCode)" }
}
if (-not (Test-Path $sdkRoot)) {
  Fail @"
Thales SDK not installed, and no -SdkMsi was given (or the path doesn't exist).

This installer never bundles the Thales SDK - it's Thales's licensed software, not ours to
redistribute (see README.md "A note on the Thales SDK itself"). You need your own copy:

  1. Get the SDK installer (.msi) from Thales directly - via your QS2000/reader purchase,
     your Thales sales contact, or whoever manages that hardware relationship at your org.
  2. Re-run this script pointing at it:
       powershell -ExecutionPolicy Bypass -File setup.ps1 -SdkMsi "C:\path\to\Thales SDK.msi"
     - or install the SDK manually first, then just run 'setup.ps1' with no -SdkMsi.

If you already installed the SDK and still see this: check it landed at
"C:\Program Files\Thales\Thales Document Reader SDK x64\<version>\" - a non-default
install location isn't currently auto-detected.
"@
}
$sdkVersionDir = Get-ChildItem $sdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkBin = Join-Path $sdkVersionDir.FullName 'Bin'
$appIni = Join-Path $sdkVersionDir.FullName 'Config\Application.ini'
if (-not (Test-Path $appIni)) { Fail "Application.ini not found under $($sdkVersionDir.FullName)" }
Write-Host "  found SDK $($sdkVersionDir.Name)" -ForegroundColor Green

# ---- 2. Application.ini: disable UV/IR (QS2000 has neither) ---------------
if ($SkipUvIrPatch) {
  Step "2/7 Disable UV/IR capture in Application.ini"
  Write-Host "  skipped (-SkipUvIrPatch) - your reader model presumably has UV/IR hardware." -ForegroundColor Yellow
} else {
  Step "2/7 Disable UV/IR capture in Application.ini (QS2000-specific; pass -SkipUvIrPatch on readers with UV/IR)"
  $ini = Get-Content $appIni -Raw
  $patched = $ini -replace '(?m)^IRImage=1', 'IRImage=0' `
                  -replace '(?m)^UVImage=1', 'UVImage=0' `
                  -replace '(?m)^IRImageRear=1', 'IRImageRear=0' `
                  -replace '(?m)^UVImageRear=1', 'UVImageRear=0'
  if ($patched -ne $ini) { Set-Content -Path $appIni -Value $patched -Encoding ASCII; Write-Host "  patched." -ForegroundColor Green }
  else { Write-Host "  already correct." -ForegroundColor Green }
}

# ---- 3. Bridge binaries ----------------------------------------------------
Step "3/7 Bridge binaries"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$prebuilt = Join-Path $scriptDir 'publish'
$projDir = Join-Path $scriptDir 'ThalesBridge'
$srcDir = $null
if (Test-Path (Join-Path $prebuilt 'ThalesBridge.exe')) {
  $srcDir = $prebuilt
  Write-Host "  using prebuilt binaries: $prebuilt"
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
  Write-Host "  publishing from source (self-contained, no .NET needed at runtime)..."
  dotnet publish $projDir -c Release -r win-x64 --self-contained true -o $prebuilt | Out-Null
  if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed (is ThalesBridge/libs/MMMReaderDotNet50.dll present?)' }
  $srcDir = $prebuilt
} else {
  Fail "no prebuilt 'publish' folder next to this script and no .NET SDK to build one. Download 'publish.zip' from Bitbucket Downloads and extract it here as 'publish\', or install the .NET 8 SDK."
}
Get-Process ThalesBridge -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item "$srcDir\*" $InstallDir -Recurse -Force
Write-Host "  installed to $InstallDir" -ForegroundColor Green

# ---- 4. Launcher (SDK Bin on PATH + writable working dir) ------------------
Step "4/7 Launcher"
$launcher = Join-Path $InstallDir 'run-bridge.cmd'
@"
@echo off
rem Auto-generated by setup.ps1 - native Thales DLLs resolve via PATH; the
rem working directory must be writable (SDK log files).
set "PATH=$sdkBin;%PATH%"
if not exist "%LOCALAPPDATA%\ThalesBridge" mkdir "%LOCALAPPDATA%\ThalesBridge"
cd /d "%LOCALAPPDATA%\ThalesBridge"
"$InstallDir\ThalesBridge.exe" $Port
"@ | Set-Content -Path $launcher -Encoding ASCII
Write-Host "  wrote $launcher" -ForegroundColor Green

# ---- 5. URL ACL so non-admin users can bind the port -----------------------
Step "5/7 URL ACL for port $Port"
# Via cmd: netsh writes to stderr when there is no existing reservation, and
# 5.1 turns native stderr into a NativeCommandError that 'Stop' makes fatal.
cmd /c "netsh http delete urlacl url=http://localhost:$Port/ >nul 2>&1"
netsh http add urlacl url="http://localhost:$Port/" user='BUILTIN\Users' | Out-Null
Write-Host "  granted BUILTIN\Users the right to listen on http://localhost:$Port/" -ForegroundColor Green

# ---- 6. Scheduled task: start at boot (or at logon with -LogonStart) --------
Step "6/7 Scheduled task"
# Registered via the cmdlets rather than `schtasks /tr`: a quoted path does not
# survive native-command argument parsing, so the default install dir
# "C:\Program Files\..." would register as Command=C:\Program and fail at run
# time with 0x80070002. The battery defaults also have to be overridden -- a
# scanner PC may be a laptop or tablet, where the defaults refuse to start (and
# would kill) the bridge on battery -- and the default 3-day execution limit
# lifted, since the bridge is meant to run indefinitely.
$taskAction = New-ScheduledTaskAction -Execute $launcher
$taskSettings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
  -ExecutionTimeLimit ([TimeSpan]::Zero)
if ($LogonStart) {
  # Runs as whoever installed it, when they log on. Needs step 5's URL ACL to bind.
  $taskTrigger = New-ScheduledTaskTrigger -AtLogOn
  $taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive -RunLevel Limited
} else {
  # SYSTEM + boot trigger: serving before/without any logon. SYSTEM is
  # admin-equivalent so HTTP.SYS lets it bind the port regardless of step 5's ACL.
  $taskTrigger = New-ScheduledTaskTrigger -AtStartup
  $taskPrincipal = New-ScheduledTaskPrincipal `
    -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
}
Register-ScheduledTask -TaskName $TaskName -Action $taskAction -Trigger $taskTrigger `
  -Principal $taskPrincipal -Settings $taskSettings -Force | Out-Null
if ($LogonStart) {
  Write-Host "  task '$TaskName' runs the bridge at logon (as $($taskPrincipal.UserId))." -ForegroundColor Green
} else {
  Write-Host "  task '$TaskName' runs the bridge at boot as SYSTEM - no logon needed." -ForegroundColor Green
}

# ---- 7. Start now and verify ------------------------------------------------
Step "7/7 Start and verify"
Start-ScheduledTask -TaskName $TaskName
$listening = $false
foreach ($i in 1..15) {
  Start-Sleep -Seconds 1
  if (Test-NetConnection -ComputerName localhost -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) {
    $listening = $true; break
  }
}
if ($listening) {
  Write-Host "`nSUCCESS - bridge is serving ws://localhost:$Port" -ForegroundColor Green
  Write-Host "Plug in the reader if it isn't yet: the bridge retries every 10s until it appears."
  Write-Host "Run 'setup.ps1 -Doctor' any time to re-check this install without reinstalling."
} else {
  Fail "bridge did not open port $Port within 15s. Run `"$launcher`" in a console to see its error message, or 'setup.ps1 -Doctor' for a full diagnostic."
}
