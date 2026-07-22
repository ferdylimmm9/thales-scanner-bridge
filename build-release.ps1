<#
.SYNOPSIS
  Builds release assets, including the double-click Windows installer.

.DESCRIPTION
  Maintainer/build-server script. The client receives ThalesBridgeSetup.exe and
  does not need PowerShell or the .NET SDK to launch the installer.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish'
$publishZip = Join-Path $root 'publish.zip'
$payloadDir = Join-Path $root '.installer-payload'
$payloadZip = Join-Path $root 'Installer\payload.zip'
$installerOut = Join-Path $root 'installer-out'
$installerExe = Join-Path $root 'ThalesBridgeSetup.exe'

try {
  dotnet publish (Join-Path $root 'ThalesBridge') -c Release -r win-x64 `
    --self-contained true -p:Version=$Version -o $publishDir
  if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }

  Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $publishZip -Force

  New-Item -ItemType Directory -Force (Join-Path $payloadDir 'publish') | Out-Null
  Copy-Item (Join-Path $publishDir '*') (Join-Path $payloadDir 'publish') -Recurse -Force
  Copy-Item (Join-Path $root 'setup.ps1') $payloadDir -Force
  Copy-Item (Join-Path $root 'uninstall.ps1') $payloadDir -Force
  Compress-Archive -Path (Join-Path $payloadDir '*') -DestinationPath $payloadZip -Force

  dotnet publish (Join-Path $root 'Installer\ThalesBridgeInstaller.csproj') `
    -c Release -r win-x64 --self-contained true -p:Version=$Version -o $installerOut
  if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

  Copy-Item (Join-Path $installerOut 'ThalesBridgeSetup.exe') $installerExe -Force
  Write-Host "Built $installerExe"
  Write-Host "Built $publishZip"
}
finally {
  Remove-Item $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
  Remove-Item $installerOut -Recurse -Force -ErrorAction SilentlyContinue
}
