# Exercise the real configuration transaction with Windows service/network calls mocked.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'setup.ps1') -Raw
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
$block = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq '$ConfigureEndpoint' }, $true)
$transaction = [scriptblock]::Create(($block.Clauses[0].Item2.Extent.Text.Trim().Substring(1).TrimEnd().TrimEnd('}') -replace 'exit 0', 'return'))
function Fail($message) { throw $message }
function Pass($message) {}
function Warn($message) { throw $message }
function Get-ScheduledTask { [pscustomobject]@{State='Running'} }
function Disable-ScheduledTask {}
function Enable-ScheduledTask {}
function Stop-ScheduledTask {}
function Start-ScheduledTask {}
function Get-Process {}
function Start-Sleep {}
function Get-NetTCPConnection { throw 'Same-port switch must not reject the current listener' }
function Set-BridgeEndpoint { param($Port,$Protocol) $script:prepared = "$Protocol/$Port" }
function Remove-BridgeEndpoint { param($Port,$Protocol) $script:removed = "$Protocol/$Port" }
function Test-BridgeWebSocket { return $probeSuccess }
$InstallDir = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
$null = New-Item -ItemType Directory $InstallDir
try {
  foreach ($oldScheme in @('ws','wss')) {
    foreach ($probeSuccess in @($true,$false)) {
      $Port = 8765; $TaskName = 'TestBridge'
      $Protocol = if ($oldScheme -eq 'ws') {'wss'} else {'ws'}
      $settingsFile = Join-Path $InstallDir 'bridge-settings.json'
      $launcherFile = Join-Path $InstallDir 'run-bridge.cmd'
      @{port=$Port;protocol=$oldScheme} | ConvertTo-Json | Set-Content $settingsFile
      $arg = if ($oldScheme -eq 'ws') {' --ws'} else {''}
      $originalLauncher = '"' + (Join-Path $InstallDir 'ThalesBridge.exe') + '" 8765' + $arg + "`r`n"
      [IO.File]::WriteAllText($launcherFile,$originalLauncher)
      $originalSettings = [IO.File]::ReadAllText($settingsFile)
      $failed = $false
      try { & $transaction } catch { if ($_.Exception.Message -notmatch 'Restored endpoint') {throw}; $failed=$true }
      if ($failed -eq $probeSuccess) {throw 'Unexpected transaction result'}
      if ($prepared -ne "$Protocol/$Port") {throw 'Wrong new reservation'}
      if ($probeSuccess) {
        if ((Get-Content $settingsFile -Raw | ConvertFrom-Json).protocol -ne $Protocol) {throw 'Protocol not persisted'}
        if (([IO.File]::ReadAllText($launcherFile).Contains('--ws')) -ne ($Protocol -eq 'ws')) {throw 'Wrong launcher protocol'}
        if ($removed -ne "$oldScheme/$Port") {throw 'Wrong old reservation cleanup'}
      } else {
        if ([IO.File]::ReadAllText($settingsFile) -cne $originalSettings -or [IO.File]::ReadAllText($launcherFile) -cne $originalLauncher) {throw 'Rollback failed'}
        if ($removed -ne "$Protocol/$Port") {throw 'Wrong rollback reservation cleanup'}
      }
      Write-Host "PASS $oldScheme -> $Protocol same-port switch; handshake success=$probeSuccess"
    }
  }
} finally { Remove-Item $InstallDir -Recurse -Force }
