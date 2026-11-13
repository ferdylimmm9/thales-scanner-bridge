<# Builds the offline Inno Setup installer and portable Windows binaries. #>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$')][string]$Version,
  [string]$Compiler = $env:ISCC
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Compiler) { $Compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $Compiler -or -not (Test-Path $Compiler)) { throw 'Install Inno Setup 7.1.1 using packaging/install-compiler.ps1 and pass -Compiler or set ISCC.' }
$publishDir = Join-Path $root 'publish'
$managerOut = Join-Path $root 'manager-out'
Remove-Item $publishDir,$managerOut -Recurse -Force -ErrorAction SilentlyContinue
try {
  dotnet publish (Join-Path $root 'ThalesBridge') -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir
  if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
  # Share runtime files with the bridge rather than embedding a duplicate runtime in Manager.
  dotnet publish (Join-Path $root 'Manager/ThalesBridgeManager.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false -p:Version=$Version -o $managerOut
  if ($LASTEXITCODE -ne 0) { throw 'Manager publish failed.' }
  Copy-Item (Join-Path $managerOut '*') $publishDir -Recurse -Force
  Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath (Join-Path $root 'publish.zip') -Force
  $fileVersion = ($Version -split '[-+]')[0] + '.0'
  & $Compiler "/DReleaseVersion=$Version" "/DFileVersion=$fileVersion" (Join-Path $root 'packaging/ThalesBridge.iss')
  if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
  if (-not (Test-Path (Join-Path $root 'ThalesBridgeSetup.exe'))) { throw 'Installer output missing.' }
  Write-Host "Built $root\ThalesBridgeSetup.exe and publish.zip"
} finally { Remove-Item $managerOut -Recurse -Force -ErrorAction SilentlyContinue }
