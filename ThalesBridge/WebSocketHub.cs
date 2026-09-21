using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

namespace ThalesBridge;

/// <summary>Localhost-only HTTPS listener; HTTP.sys manages the TLS certificate.</summary>
public sealed class WebSocketHub : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    public Func<string, Task<ScannerMessage>>? CommandReceived { get; set; }
    private readonly HttpListener _listener = new();
    private readonly Dictionary<Guid, WebSocketClient> _clients = new();
    private readonly HashSet<Task> _handlers = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly int _port;
    private Task? _acceptTask;
    private byte[] _lastStatus = Serialize(ScannerMessage.StatusMsg("idle", "automatic"));

    public WebSocketHub(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _listener.Prefixes.Add($"https://localhost:{port}/");
    }

    public void Start()
    {
        try { _listener.Start(); }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not listen on https://localhost:{_port}/ (Windows error {ex.ErrorCode}). "
                + "Run setup.ps1 as Administrator to configure the HTTPS URL ACL and TLS certificate; "
                + $"check for another bridge with: netstat -ano | findstr :{_port}", ex);
        }
        _acceptTask = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (_cts.IsCancellationRequested) { break; }
            catch (HttpListenerException ex)
            {
                Console.Error.WriteLine($"[bridge] listener failed: {ex.Message}");
                // Let the scheduled task restart the process instead of silently staying offline.
                Environment.Exit(1);
                return;
            }
            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 426;
                ctx.Response.Close();
                continue;
            }
            var handler = HandleClientAsync(ctx);
            lock (_gate) _handlers.Add(handler);
            _ = handler.ContinueWith(t => { lock (_gate) _handlers.Remove(t); }, TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext ctx)
    {
        var id = Guid.NewGuid();
        WebSocketClient? client = null;
        try
        {
            using var abortHandshake = _cts.Token.Register(() => { try { ctx.Response.Abort(); } catch { } });
            var ws = await ctx.AcceptWebSocketAsync(null, 4096, TimeSpan.FromSeconds(20));
            client = new WebSocketClient(ws.WebSocket, async json =>
            {
                var handler = CommandReceived;
                if (handler != null) client!.Enqueue(Serialize(await handler(json)));
            });
            lock (_gate)
            {
                if (_cts.IsCancellationRequested) return;
                // Queue greeting before making this client visible to scanner callbacks.
                client.Enqueue(_lastStatus);
                _clients.Add(id, client);
            }
            Console.WriteLine($"[bridge] browser connected ({id}).");
            await client.RunAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            if (!_cts.IsCancellationRequested)
                Console.WriteLine($"[bridge] browser {id}: {ex.Message}");
            if (client == null) { try { ctx.Response.Abort(); } catch { } }
        }
        finally
        {
            lock (_gate) _clients.Remove(id);
            client?.Dispose();
            Console.WriteLine($"[bridge] browser disconnected ({id}).");
        }
    }

    public Task BroadcastAsync(ScannerMessage message)
    {
        lock (_gate)
        {
            if (_cts.IsCancellationRequested) return Task.CompletedTask;
            var payload = Serialize(message);
            if (message.Type == "status") _lastStatus = payload;
            foreach (var client in _clients.Values) client.Enqueue(payload);
        }
        return Task.CompletedTask;
    }

    private static byte[] Serialize(ScannerMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Close();
        if (_acceptTask != null) await _acceptTask;
        Task[] handlers;
        lock (_gate) handlers = _handlers.ToArray();
        await Task.WhenAll(handlers);
        _cts.Dispose();
    }
}
