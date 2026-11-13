$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($file in (Get-ChildItem $root -Filter '*.ps1' -Recurse | Where-Object { $_.FullName -notmatch '[\\/](publish|bin|obj|node_modules|\.wine-build)[\\/]' })) {
  $parseErrors = $null
  $tokens = $null
  $null = [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
  if ($parseErrors.Count -gt 0) { throw "$($file.Name): $($parseErrors -join [Environment]::NewLine)" }
  Write-Host "PASS PowerShell syntax: $($file.Name)"
}
