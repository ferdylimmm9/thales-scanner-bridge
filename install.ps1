<#
.SYNOPSIS
  One-liner bootstrap: downloads the latest release and runs setup.ps1.

.DESCRIPTION
  For a kiosk PC with nothing on it yet — no git clone, no .NET SDK required.
  Fetches the latest GitHub Release's publish.zip + setup.ps1 into a temp
  folder, self-elevates if needed, and runs the full install.

  As with any "download and run" installer (rustup, deno, etc.), review the
  script at the URL below before piping it to a shell you don't fully trust.
  This one only downloads from and talks to github.com and localhost.

.EXAMPLE
  irm https://raw.githubusercontent.com/REPLACE_ME/thales-scanner-bridge/main/install.ps1 | iex
#>
[CmdletBinding()]
param(
  [int]$Port = 8765,
  [switch]$SkipUvIrPatch
)

$ErrorActionPreference = 'Stop'
$Repo = 'REPLACE_ME/thales-scanner-bridge'  # <org>/<repo> on GitHub

# ---- self-elevate if not already admin -------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
  ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Host "Relaunching elevated (UAC prompt incoming)..." -ForegroundColor Yellow
  $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
    "irm https://raw.githubusercontent.com/$Repo/main/install.ps1 | iex")
  Start-Process powershell -Verb RunAs -ArgumentList $argsList -Wait
  exit $LASTEXITCODE
}

# ---- download latest release: setup.ps1 + ThalesBridge/ + publish.zip -----
Write-Host "Fetching latest release of $Repo..." -ForegroundColor Cyan
$release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
$tag = $release.tag_name
$work = Join-Path $env:TEMP "thales-scanner-bridge-$tag"
New-Item -ItemType Directory -Force $work | Out-Null

$publishAsset = $release.assets | Where-Object { $_.name -eq 'publish.zip' } | Select-Object -First 1
if (-not $publishAsset) { throw "Release $tag has no publish.zip asset." }
$publishZip = Join-Path $work 'publish.zip'
Invoke-WebRequest $publishAsset.browser_download_url -OutFile $publishZip
Expand-Archive $publishZip (Join-Path $work 'publish') -Force

Invoke-WebRequest "https://raw.githubusercontent.com/$Repo/$tag/setup.ps1" -OutFile (Join-Path $work 'setup.ps1')

# ---- hand off to setup.ps1 (does the real work; safe to re-run) -----------
Write-Host "Running setup.ps1 from $work ..." -ForegroundColor Cyan
$setupArgs = @('-Port', $Port)
if ($SkipUvIrPatch) { $setupArgs += '-SkipUvIrPatch' }
& (Join-Path $work 'setup.ps1') @setupArgs
