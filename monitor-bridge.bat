@echo off
setlocal
title Thales Scanner Bridge Monitor

echo Thales Scanner Bridge Monitor
echo Connecting to ws://localhost:8765/ ...
echo Press Ctrl+C to stop.
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "& { ^
    $ErrorActionPreference = 'Stop'; ^
    $socket = [System.Net.WebSockets.ClientWebSocket]::new(); ^
    $cancel = [System.Threading.CancellationToken]::None; ^
    try { ^
      $socket.ConnectAsync([Uri]'ws://localhost:8765/', $cancel).GetAwaiter().GetResult(); ^
      Write-Host 'Bridge: CONNECTED' -ForegroundColor Green; ^
      $buffer = New-Object byte[] (2 * 1024 * 1024); ^
      $scanCount = 0; ^
      while ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) { ^
        $length = 0; ^
        do { ^
          if ($length -ge $buffer.Length) { throw 'WebSocket message exceeded the 2 MB monitor limit.'; } ^
          $segment = [ArraySegment[byte]]::new($buffer, $length, $buffer.Length - $length); ^
          $received = $socket.ReceiveAsync($segment, $cancel).GetAwaiter().GetResult(); ^
          $length += $received.Count; ^
        } while (-not $received.EndOfMessage); ^
        if ($received.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) { break; } ^
        $message = [Text.Encoding]::UTF8.GetString($buffer, 0, $length) | ConvertFrom-Json; ^
        $time = Get-Date -Format 'HH:mm:ss'; ^
        if ($message.type -eq 'status') { ^
          switch ($message.status) { ^
            'idle' { Write-Host ('[' + $time + '] Scanner: NOT READY') -ForegroundColor Yellow; break; } ^
            'waiting_for_document' { Write-Host ('[' + $time + '] Scanner: READY - place a document on the reader') -ForegroundColor Cyan; break; } ^
            'reading' { Write-Host ('[' + $time + '] Scanner: READING DOCUMENT') -ForegroundColor Yellow; break; } ^
            default { Write-Host ('[' + $time + '] Scanner status: ' + $message.status) -ForegroundColor Cyan; } ^
          } ^
        } elseif ($message.type -eq 'result') { ^
          $scanCount++; ^
          Write-Host ('[' + $time + '] SCAN COMPLETED (' + $scanCount + ' this session)') -ForegroundColor Green; ^
        } elseif ($message.type -eq 'error') { ^
          Write-Host ('[' + $time + '] ERROR ' + $message.code + ': ' + $message.message) -ForegroundColor Red; ^
        } ^
      } ^
      Write-Host 'Bridge: DISCONNECTED' -ForegroundColor Red; ^
    } catch { ^
      Write-Host ('Bridge: CONNECTION FAILED - ' + $_.Exception.Message) -ForegroundColor Red; ^
      Write-Host 'Confirm ThalesBridge.exe is running and port 8765 is available.' -ForegroundColor Yellow; ^
      exit 1; ^
    } finally { ^
      if ($null -ne $socket) { $socket.Dispose(); } ^
    } ^
  }"

echo.
pause
endlocal
