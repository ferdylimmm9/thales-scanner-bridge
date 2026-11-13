# Exercise saved-port resolution without invoking Windows uninstall operations.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'uninstall.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
$node = $ast.Find({ param($item) $item -is [System.Management.Automation.Language.IfStatementAst] -and $item.Clauses[0].Item1.Extent.Text -like '*PSBoundParameters*' }, $true)
$resolve = [scriptblock]::Create('param([int]$Port = 8765)' + [Environment]::NewLine + $node.Extent.Text + [Environment]::NewLine + 'return $Port')
$InstallDir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
$originalEnvPort = $env:THALES_PORT
try {
  $env:THALES_PORT = ''
  $null = New-Item -ItemType Directory $InstallDir
  if ((& $resolve) -ne 8765) { throw 'Fresh uninstall default changed' }
  @{port=9001;protocol='ws'} | ConvertTo-Json | Set-Content (Join-Path $InstallDir 'bridge-settings.json')
  if ((& $resolve) -ne 9001) { throw 'Installed port not resolved' }
  if ((& $resolve -Port 9010) -ne 9010) { throw 'Explicit port not respected' }
  @{port=0} | ConvertTo-Json | Set-Content (Join-Path $InstallDir 'bridge-settings.json')
  $rejected = $false
  try { & $resolve | Out-Null } catch { $rejected = $true }
  if (-not $rejected) { throw 'Invalid installed port accepted' }
  Write-Host 'PASS uninstall default, saved port, explicit override and invalid settings'
} finally {
  $env:THALES_PORT = $originalEnvPort
  Remove-Item $InstallDir -Recurse -Force
}
