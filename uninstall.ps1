<#
.SYNOPSIS
  Removes the Thales scanner bridge from this PC.

.DESCRIPTION
  Self-elevates if needed, then undoes everything setup.ps1 installed:
    1. Stops and deletes the 'ThalesBridge' Scheduled Task (boot or logon).
    2. Kills any running ThalesBridge.exe.
    3. Removes the URL ACL reservation for the port.
    4. Deletes C:\Program Files\ThalesBridge.
    5. Deletes the bridge's working/log folders (SDK logs may contain document
       data, so they are worth removing) — skip with -KeepLogs.

  Deliberately NOT touched:
    * The Thales Document Reader SDK — it is a separate product, may be used by
      other apps, and this script never installed it in the first place.
    * The SDK's Application.ini UV/IR patch — setup.ps1 edits values in place
      and does not record the originals, so there is nothing safe to restore.
      Reinstall the SDK if you need factory settings back.

  This script is self-contained: it needs no clone, no release, and no network.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File uninstall.ps1
  powershell -ExecutionPolicy Bypass -File uninstall.ps1 -Port 9000
  powershell -ExecutionPolicy Bypass -File uninstall.ps1 -KeepLogs
  irm https://raw.githubusercontent.com/ferdylimmm9/thales-scanner-bridge/main/uninstall.ps1 | iex
#>
[CmdletBinding()]
param(
  [int]$Port = $(if ($env:THALES_PORT) { [int]$env:THALES_PORT } else { 8765 }),
  [switch]$KeepLogs
)

$ErrorActionPreference = 'Stop'
$InstallDir = 'C:\Program Files\ThalesBridge'
$TaskName = 'ThalesBridge'

function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }
function Done($msg) { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Skip($msg) { Write-Host "  [--] $msg" -ForegroundColor DarkGray }

# ---- self-elevate if not already admin ------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Host "Relaunching elevated (UAC prompt incoming)..." -ForegroundColor Yellow
  $env:THALES_PORT = $Port
  $env:THALES_KEEP_LOGS = if ($KeepLogs) { '1' } else { '' }
  if ($PSCommandPath) {
    # Running from a file (clone or downloaded copy) — re-run that same file.
    $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Port', $Port)
    if ($KeepLogs) { $argsList += '-KeepLogs' }
  } else {
    # Piped in via `irm ... | iex` — there is no file to re-run, so re-fetch.
    $bootstrap = @"
`$Port = [int]`$env:THALES_PORT
`$KeepLogs = [bool]`$env:THALES_KEEP_LOGS
irm https://raw.githubusercontent.com/ferdylimmm9/thales-scanner-bridge/main/uninstall.ps1 | iex
"@
    $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $bootstrap)
  }
  Start-Process powershell -Verb RunAs -ArgumentList $argsList -Wait
  exit $LASTEXITCODE
}

Write-Host "Thales scanner bridge — uninstall" -ForegroundColor Cyan

# ---- 1. Scheduled task -----------------------------------------------------
Step "1/5 Scheduled task"
schtasks /query /tn $TaskName 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
  schtasks /end /tn $TaskName 2>$null | Out-Null
  schtasks /delete /tn $TaskName /f 2>$null | Out-Null
  Done "removed task '$TaskName'"
} else { Skip "no task '$TaskName' registered" }

# ---- 2. Running process ----------------------------------------------------
# The task may run as SYSTEM (boot-start), so this needs the elevation above.
Step "2/5 Running process"
$proc = Get-Process ThalesBridge -ErrorAction SilentlyContinue
if ($proc) {
  $proc | Stop-Process -Force -Confirm:$false
  Done "stopped ThalesBridge.exe (PID $($proc.Id -join ', '))"
} else { Skip "ThalesBridge.exe is not running" }

# ---- 3. URL ACL ------------------------------------------------------------
Step "3/5 URL ACL for port $Port"
# Match the URL itself, not netsh's surrounding prose — that prose is localised
# and a non-English Windows would otherwise look like "nothing reserved".
$url = "http://localhost:$Port/"
$acl = (netsh http show urlacl url=$url 2>$null) -join "`n"
if ($acl -match [regex]::Escape($url)) {
  netsh http delete urlacl url=$url 2>$null | Out-Null
  Done "removed reservation for $url"
} else { Skip "no reservation for $url (was it installed on another port?)" }

# ---- 4. Install directory --------------------------------------------------
Step "4/5 Install directory"
if (Test-Path $InstallDir) {
  Remove-Item -Recurse -Force $InstallDir
  Done "deleted $InstallDir"
} else { Skip "$InstallDir does not exist" }

# ---- 5. Working/log folders ------------------------------------------------
# One per account that ever ran the bridge: the kiosk user's, plus SYSTEM's
# (systemprofile) when it was installed boot-start.
Step "5/5 Working/log folders"
if ($KeepLogs) {
  Skip "kept (-KeepLogs)"
} else {
  $workDirs = @(
    (Join-Path $env:LOCALAPPDATA 'ThalesBridge'),
    'C:\Windows\System32\config\systemprofile\AppData\Local\ThalesBridge'
  ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
  if ($workDirs) {
    foreach ($d in $workDirs) { Remove-Item -Recurse -Force $d; Done "deleted $d" }
  } else { Skip "no working/log folders found" }
}

Write-Host "`nUninstalled. (The Thales SDK and its Application.ini were left untouched.)" -ForegroundColor Green
Write-Host "Other user profiles may still hold ThalesBridge log folders under %LOCALAPPDATA%."
