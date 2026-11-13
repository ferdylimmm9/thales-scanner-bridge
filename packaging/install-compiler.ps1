# Pin the official compiler and verify its release digest and Authenticode signature.
[CmdletBinding()]
param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'ThalesBuildTools\InnoSetup-7.1.1'))
$ErrorActionPreference = 'Stop'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe'
$sha256 = '8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0'
$download = Join-Path ([IO.Path]::GetTempPath()) ('inno-' + [guid]::NewGuid() + '.exe')
try {
  Invoke-WebRequest $url -OutFile $download
  if ((Get-FileHash $download -Algorithm SHA256).Hash -ne $sha256) { throw 'Inno compiler checksum mismatch.' }
  $signature = Get-AuthenticodeSignature $download
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -ne 'Pyrsys B.V.') {
    throw 'Inno compiler signature or publisher verification failed.'
  }
  $process = Start-Process $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',('/DIR="' + $Destination + '"')) -Wait -PassThru
  if ($process.ExitCode -ne 0) { throw "Compiler install failed: $($process.ExitCode)" }
  $compiler = Join-Path $Destination 'ISCC.exe'
  if (-not (Test-Path $compiler)) { throw 'ISCC.exe missing.' }
  Write-Output $compiler
} finally { Remove-Item $download -ErrorAction SilentlyContinue }
