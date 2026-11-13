# Elevated installer integration. Inno owns binaries, shortcuts and uninstall registration.
[CmdletBinding()]
param(
  [ValidateSet('Prepare','Configure','Resume','Remove')][string]$Action,
  [string]$Endpoint = '',
  [string]$SdkMsi = '',
  [switch]$SkipUvIrPatch
)
$ErrorActionPreference = 'Stop'
$InstallDir = Join-Path $env:ProgramFiles 'ThalesBridge'
$logDirectory = Join-Path $env:ProgramData 'ThalesBridge\InstallerLogs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ("{0}-{1}.log" -f $Action,(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Start-Transcript -Path $logPath -Force | Out-Null
$taskModified = $false
try {
  Write-Host "Installer action: $Action; logs: $logPath"
  switch ($Action) {
    'Prepare' {
      # Prerequisites are checked before stopping an existing installation.
      $sdkRoot = Join-Path $env:ProgramFiles 'Thales\Thales Document Reader SDK x64'
      if (-not (Test-Path $sdkRoot) -and (-not $SdkMsi -or -not (Test-Path -LiteralPath $SdkMsi -PathType Leaf))) {
        throw 'The licensed Thales SDK is required. Select its MSI in the setup wizard or install it first.'
      }
      $task = Get-ScheduledTask -TaskName ThalesBridge -ErrorAction SilentlyContinue
      @{ wasRunning=($task -and $task.State -eq 'Running'); wasEnabled=($task -and $task.State -ne 'Disabled') } |
        ConvertTo-Json | Set-Content (Join-Path $logDirectory 'previous-task.json')
      if ($task) {
        $wasRunning = $task.State -eq 'Running'
        Disable-ScheduledTask -TaskName ThalesBridge | Out-Null
        $taskModified = $true
        if ($wasRunning) { Stop-ScheduledTask -TaskName ThalesBridge }
      }
      Get-Process ThalesBridge,ThalesBridgeManager -ErrorAction SilentlyContinue | Stop-Process -Force
      foreach ($attempt in 1..20) {
        if (-not (Get-Process ThalesBridge,ThalesBridgeManager -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
      }
      if (Get-Process ThalesBridge,ThalesBridgeManager -ErrorAction SilentlyContinue) { throw 'Bridge processes did not stop. Close Scanner Manager and retry.' }
    }
    'Configure' {
      $options = @{ ManagedInstall=$true }
      $settingsPath = Join-Path $InstallDir 'bridge-settings.json'
      if (Test-Path $settingsPath) {
        $saved = Get-Content $settingsPath -Raw | ConvertFrom-Json
        $options.Port = [int]$saved.port
        $options.Protocol = if ($saved.protocol -eq 'ws') { 'ws' } else { 'wss' }
      }
      if ($Endpoint) {
        $uri = [Uri]$Endpoint
        if (-not $uri.IsAbsoluteUri -or $uri.Scheme -notin @('ws','wss') -or $uri.Host -ne 'localhost' -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment -or $uri.UserInfo -or $uri.Port -lt 1 -or $uri.Port -gt 65535) {
          throw 'Use ws://localhost:PORT/ or wss://localhost:PORT/ without credentials, query or path.'
        }
        $options.Port = $uri.Port; $options.Protocol = $uri.Scheme
      }
      if ($SdkMsi) { $options.SdkMsi = $SdkMsi }
      if ($SkipUvIrPatch) { $options.SkipUvIrPatch = $true }
      # A child process isolates setup.ps1's exit statements so the transcript closes.
      $arguments = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',(Join-Path $InstallDir 'setup.ps1'))
      foreach ($key in $options.Keys) { $arguments += "-$key"; if ($options[$key] -isnot [bool]) { $arguments += [string]$options[$key] } }
      & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @arguments
      if ($LASTEXITCODE -ne 0) { throw "Bridge configuration failed (exit $LASTEXITCODE). See $logPath" }
    }
    'Resume' {
      $statePath = Join-Path $logDirectory 'previous-task.json'
      if ((Test-Path $statePath) -and (Get-ScheduledTask -TaskName ThalesBridge -ErrorAction SilentlyContinue)) {
        $state = Get-Content $statePath -Raw | ConvertFrom-Json
        if ($state.wasEnabled) { Enable-ScheduledTask -TaskName ThalesBridge | Out-Null }
        if ($state.wasRunning) { Start-ScheduledTask -TaskName ThalesBridge }
      }
    }
    'Remove' {
      & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $InstallDir 'uninstall.ps1') -KeepFiles -KeepLogs
      if ($LASTEXITCODE -ne 0) { throw "Bridge cleanup failed (exit $LASTEXITCODE). See $logPath" }
    }
  }
} catch {
  Write-Host "ERROR: $($_.Exception)"
  if ($Action -eq 'Prepare' -and $taskModified) {
    try {
      $state = Get-Content (Join-Path $logDirectory 'previous-task.json') -Raw | ConvertFrom-Json
      if ($state.wasEnabled) { Enable-ScheduledTask -TaskName ThalesBridge | Out-Null }
      if ($state.wasRunning) { Start-ScheduledTask -TaskName ThalesBridge }
    } catch { Write-Host "Task recovery also failed: $($_.Exception.Message)" }
  }
  exit 1
} finally { Stop-Transcript | Out-Null }
