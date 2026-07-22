<#
.SYNOPSIS
  One-liner bootstrap: downloads the latest Bitbucket build and runs setup.ps1.

.DESCRIPTION
  For a kiosk PC with nothing on it yet - no git clone, no .NET SDK required.
  Fetches publish.zip from Bitbucket Downloads plus setup.ps1 into a temp
  folder, self-elevates if needed, and runs the full install.

  As with any "download and run" installer (rustup, deno, etc.), review the
  script at the URL below before piping it to a shell you don't fully trust.
  This one only downloads from and talks to bitbucket.org and localhost.

  This script never bundles the Thales SDK itself - pass -SdkMsi if you
  already have the installer locally, otherwise install the SDK yourself
  first. See README.md "A note on the Thales SDK itself" for why.

.EXAMPLE
  irm https://bitbucket.org/solaireresortcasino/thales-scanner-bridge/raw/main/install.ps1 | iex
  # with params, since `iex` can't take them directly:
  $env:THALES_SDK_MSI = "C:\path\to\Thales SDK.msi"; irm https://bitbucket.org/solaireresortcasino/thales-scanner-bridge/raw/main/install.ps1 | iex
#>
[CmdletBinding()]
param(
  [string]$SdkMsi = $env:THALES_SDK_MSI,
  [int]$Port = 8765,
  [switch]$SkipUvIrPatch,
  [switch]$LogonStart
)

$ErrorActionPreference = 'Stop'
$RepoUrl = 'https://bitbucket.org/solaireresortcasino/thales-scanner-bridge'

# ---- self-elevate if not already admin, forwarding every param ------------
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Host "Relaunching elevated (UAC prompt incoming)..." -ForegroundColor Yellow
  # Params are passed via env vars rather than re-quoting them into a nested
  # -Command string - avoids quoting bugs for paths with spaces (SdkMsi).
  $env:THALES_SDK_MSI = $SdkMsi
  $env:THALES_PORT = $Port
  $env:THALES_SKIP_UV_IR = if ($SkipUvIrPatch) { '1' } else { '' }
  $env:THALES_LOGON_START = if ($LogonStart) { '1' } else { '' }
  $bootstrap = @"
`$SdkMsi = `$env:THALES_SDK_MSI
`$Port = [int]`$env:THALES_PORT
`$SkipUvIrPatch = [bool]`$env:THALES_SKIP_UV_IR
`$LogonStart = [bool]`$env:THALES_LOGON_START
irm $RepoUrl/raw/main/install.ps1 | iex
"@
  $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $bootstrap)
  Start-Process powershell -Verb RunAs -ArgumentList $argsList -Wait
  exit $LASTEXITCODE
}

# ---- download current setup.ps1 + publish.zip from Bitbucket ---------------
Write-Host "Fetching the latest Thales scanner bridge build..." -ForegroundColor Cyan
$work = Join-Path $env:TEMP 'thales-scanner-bridge-latest'
New-Item -ItemType Directory -Force $work | Out-Null

$publishZip = Join-Path $work 'publish.zip'
Invoke-WebRequest "$RepoUrl/downloads/publish.zip" -OutFile $publishZip
Expand-Archive $publishZip (Join-Path $work 'publish') -Force

Invoke-WebRequest "$RepoUrl/raw/main/setup.ps1" -OutFile (Join-Path $work 'setup.ps1')
# Fetched alongside so the PC has a working uninstaller offline, and so
# `setup.ps1 -Uninstall` finds it adjacent rather than using its inline fallback.
Invoke-WebRequest "$RepoUrl/raw/main/uninstall.ps1" -OutFile (Join-Path $work 'uninstall.ps1')

# ---- hand off to setup.ps1 (does the real work; safe to re-run) -----------
Write-Host "Running setup.ps1 from $work ..." -ForegroundColor Cyan
$setupArgs = @('-Port', $Port)
if ($SdkMsi) { $setupArgs += @('-SdkMsi', $SdkMsi) }
if ($SkipUvIrPatch) { $setupArgs += '-SkipUvIrPatch' }
if ($LogonStart) { $setupArgs += '-LogonStart' }
& (Join-Path $work 'setup.ps1') @setupArgs
