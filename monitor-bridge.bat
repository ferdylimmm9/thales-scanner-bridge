@echo off
setlocal
title Thales Scanner Bridge Monitor

echo Thales Scanner Bridge Monitor
echo Connecting to wss://localhost:8765/ ...
echo Press Ctrl+C to stop monitoring.
echo.

where powershell.exe >nul 2>&1
if errorlevel 1 (
  echo ERROR: Windows PowerShell was not found.
  echo.
  pause
  exit /b 1
)

set "THALES_MONITOR_FILE=%~f0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$file=[Environment]::GetEnvironmentVariable('THALES_MONITOR_FILE');$text=[IO.File]::ReadAllText($file);$marker=':'+'POWERSHELL_MONITOR';$start=$text.LastIndexOf($marker);if($start -lt 0){throw 'Embedded monitor was not found.'};&([scriptblock]::Create($text.Substring($start+$marker.Length)))"
set "THALES_MONITOR_EXIT=%ERRORLEVEL%"

echo.
if not "%THALES_MONITOR_EXIT%"=="0" (
  echo The monitor stopped with an error. Review the message above.
) else (
  echo The monitor has stopped.
)
echo Press any key to close this window.
pause >nul
exit /b %THALES_MONITOR_EXIT%

:POWERSHELL_MONITOR
$ErrorActionPreference = 'Stop'
$exitCode = 0
$socket = New-Object System.Net.WebSockets.ClientWebSocket
$cancel = [System.Threading.CancellationToken]::None

try {
    $socket.ConnectAsync([Uri]'wss://localhost:8765/', $cancel).GetAwaiter().GetResult()
    Write-Host 'Bridge: CONNECTED' -ForegroundColor Green

    $buffer = New-Object byte[] (8 * 1024 * 1024)
    $scanCount = 0

    while ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $length = 0

        do {
            if ($length -ge $buffer.Length) {
                throw 'WebSocket message exceeded the 8 MB monitor limit.'
            }

            $segment = [System.ArraySegment[byte]]::new(
                $buffer,
                $length,
                ($buffer.Length - $length)
            )
            $received = $socket.ReceiveAsync($segment, $cancel).GetAwaiter().GetResult()
            $length += $received.Count
        } while (-not $received.EndOfMessage)

        if ($received.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            break
        }

        $message = [Text.Encoding]::UTF8.GetString($buffer, 0, $length) | ConvertFrom-Json
        $time = Get-Date -Format 'HH:mm:ss'

        if ($message.type -eq 'status') {
            switch ($message.status) {
                'idle' {
                    Write-Host "[$time] Scanner: NOT READY" -ForegroundColor Yellow
                }
                'waiting_for_document' {
                    Write-Host "[$time] Scanner: READY - place a document on the reader" -ForegroundColor Cyan
                }
                'reading' {
                    Write-Host "[$time] Scanner: READING DOCUMENT" -ForegroundColor Yellow
                }
                default {
                    Write-Host "[$time] Scanner status: $($message.status)" -ForegroundColor Cyan
                }
            }
        }
        elseif ($message.type -eq 'result') {
            $scanCount++
            Write-Host "[$time] SCAN COMPLETED ($scanCount this session)" -ForegroundColor Green
        }
        elseif ($message.type -eq 'error') {
            Write-Host "[$time] ERROR $($message.code): $($message.message)" -ForegroundColor Red
        }
    }

    Write-Host 'Bridge: DISCONNECTED' -ForegroundColor Red
}
catch {
    $exitCode = 1
    Write-Host "Bridge: CONNECTION FAILED - $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Confirm ThalesBridge.exe is running and port 8765 is available.' -ForegroundColor Yellow
}
finally {
    if ($null -ne $socket) {
        $socket.Dispose()
    }
}

exit $exitCode
