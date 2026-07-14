using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ThalesBridge;

/// <summary>
/// Minimal localhost WebSocket server built on HttpListener (no ASP.NET dependency).
/// Accepts browser clients and broadcasts JSON frames to all of them.
/// This is the ONLY part of the bridge the React app talks to.
/// </summary>
public sealed class WebSocketHub : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly int _port;
    private ScannerMessage _lastStatus = ScannerMessage.StatusMsg("idle");

    public WebSocketHub(int port)
    {
        _port = port;
        // Localhost only — never bind to 0.0.0.0. The scanner PC serves only its own browser.
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    /// <exception cref="InvalidOperationException">
    /// Port unusable — the message says why (in use / needs urlacl) and how to fix it.
    /// </exception>
    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            // Turn the two setup failures every new machine can hit into actionable text.
            var hint = ex.ErrorCode switch
            {
                5 => $"Access denied binding http://localhost:{_port}/ — this Windows user may not "
                   + $"register the port. Fix (run once, as admin): "
                   + $"netsh http add urlacl url=http://localhost:{_port}/ user={Environment.UserDomainName}\\{Environment.UserName}",
                32 or 183 => $"Port {_port} is already in use — most likely another ThalesBridge instance "
                           + $"is running. Find the owner with: netstat -ano | findstr :{_port}",
                _ => $"Could not listen on http://localhost:{_port}/ (win32 error {ex.ErrorCode}): {ex.Message}",
            };
            throw new InvalidOperationException(hint, ex);
        }

        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (_cts.IsCancellationRequested) { break; }
            catch (HttpListenerException) { break; }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 426; // Upgrade Required
                ctx.Response.Close();
                continue;
            }

            _ = HandleClientAsync(ctx);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext ctx)
    {
        WebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null); }
        catch { ctx.Response.StatusCode = 500; ctx.Response.Close(); return; }

        var socket = wsCtx.WebSocket;
        var id = Guid.NewGuid();
        _clients[id] = socket;

        // Greet the new client with the current reader status so the UI is correct immediately.
        await SendToAsync(socket, _lastStatus);

        try
        {
            // We don't expect client->server messages in v1, but we must drain to detect close.
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var res = await socket.ReceiveAsync(buffer, _cts.Token);
                if (res.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch { /* client vanished */ }
        finally
        {
            _clients.TryRemove(id, out _);
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
            catch { /* already gone */ }
            socket.Dispose();
        }
    }

    /// <summary>Broadcast a frame to every connected client. Called from scanner callbacks.</summary>
    public async Task BroadcastAsync(ScannerMessage message)
    {
        if (message.Type == "status") _lastStatus = message;

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);
        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open) { _clients.TryRemove(id, out _); continue; }
            try
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, _cts.Token);
            }
            catch
            {
                _clients.TryRemove(id, out _);
            }
        }
    }

    private static async Task SendToAsync(WebSocket socket, ScannerMessage message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);
        try { await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var (_, socket) in _clients)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "shutdown", CancellationToken.None); }
            catch { /* ignore */ }
        }
        try { _listener.Stop(); } catch { /* ignore */ }
        _listener.Close();
        _cts.Dispose();
    }
}
