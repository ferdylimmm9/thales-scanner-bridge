# Execute the real registration block with native task cmdlets mocked.
$ErrorActionPreference='Stop'
$source = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'setup.ps1') -Raw
$start = $source.IndexOf('$taskAction = New-ScheduledTaskAction')
$end = $source.IndexOf('# ---- 7.', $start)
if ($end -lt 0) { $end = $source.IndexOf('Start-ScheduledTask -TaskName $TaskName', $start) }
$registration = [scriptblock]::Create(($source.Substring($start,$end-$start)).Replace('[Security.Principal.WindowsIdentity]::GetCurrent().Name', "'TEST\Installer'"))
function New-ScheduledTaskAction { param($Execute) return @{Execute=$Execute} }
function New-ScheduledTaskSettingsSet { param([switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries,$ExecutionTimeLimit,$RestartCount,$RestartInterval) return @{} }
function New-ScheduledTaskTrigger { param([switch]$AtStartup,[switch]$AtLogOn) return @{Boot=[bool]$AtStartup;Logon=[bool]$AtLogOn} }
function New-ScheduledTaskPrincipal { param($UserId,$LogonType,$RunLevel) return @{UserId=$UserId;RunLevel=$RunLevel} }
function Register-ScheduledTask { param($TaskName,$Action,$Principal,$Settings,[switch]$Force,$Trigger) $script:captured=@{Principal=$Principal;Trigger=$Trigger} }
function Enable-ScheduledTask {}
$TaskName='ThalesBridge'; $launcher='C:\Test\run-bridge.cmd'
foreach ($mode in @('boot','logon','manual')) {
 $NoAutoStart = $mode -eq 'manual'; $LogonStart = $mode -eq 'logon'
 & $registration
 if ($mode -eq 'manual' -and $captured.Trigger) { throw 'Manual mode registered a startup trigger' }
 if ($mode -eq 'boot' -and (-not $captured.Trigger.Boot -or $captured.Principal.UserId -ne 'SYSTEM')) { throw 'Boot registration incorrect' }
 if ($mode -eq 'logon' -and (-not $captured.Trigger.Logon -or $captured.Principal.RunLevel -ne 'Limited')) { throw 'Logon registration incorrect' }
 Write-Host "PASS startup mode: $mode"
}

# Managed install removes the old task and returns before the legacy task registration.
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
$managed = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq '$ManagedInstall' -and $node.Extent.Text -match 'Unregister-ScheduledTask' }, $true)
$installStep = [scriptblock]::Create(($managed.Extent.Text -replace 'exit 0','return'))
function Get-ScheduledTask { [pscustomobject]@{State='Disabled'} }
function Unregister-ScheduledTask { param($TaskName,[switch]$Confirm) $script:removedTask = $TaskName }
function Remove-Item { param($Path,[switch]$Force) $script:removedLauncher = $Path }
function Pass($message) {}
$InstallDir = [IO.Path]::GetTempPath()
$ManagedInstall = $true
& $installStep
if ($removedTask -ne 'ThalesBridge' -or $removedLauncher -ne (Join-Path $InstallDir 'run-bridge.cmd')) { throw 'Legacy startup configuration not removed' }
Write-Host 'PASS managed install removes legacy startup task and launcher'
