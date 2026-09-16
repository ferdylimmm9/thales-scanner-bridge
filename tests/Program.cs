using System.Net.WebSockets;
using System.Text;
using ThalesBridge;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

foreach (var invalid in new[] { "ws://localhost:8765/", "wss://example.com:8765/", "wss://127.0.0.1:8765/", "wss://localhost:0/", "wss://localhost:65536/", "wss://user@localhost:8765/", "wss://localhost:8765/path", "wss://localhost:8765/?x=1", "wss://localhost:8765/#fragment" })
    Check(!ThalesBridgeInstaller.EndpointSettings.TryGetLocalPort(invalid, out _), $"Accepted invalid endpoint: {invalid}");
Check(ThalesBridgeInstaller.EndpointSettings.TryGetLocalPort(" wss://localhost:9000/ ", out var editedPort) && editedPort == 9000, "Valid endpoint rejected");
Console.WriteLine("PASS endpoint validation rejects insecure, remote and malformed endpoints");

// Greeting, result and status must arrive in order, without concurrent sends.
using (var socket = new ProbeSocket())
using (var client = new WebSocketClient(socket))
using (var stop = new CancellationTokenSource())
{
    client.Enqueue(Encoding.UTF8.GetBytes("greeting"));
    var running = client.RunAsync(stop.Token);
    client.Enqueue(Encoding.UTF8.GetBytes("result"));
    client.Enqueue(Encoding.UTF8.GetBytes("status"));
    await socket.ThreeSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
    stop.Cancel();
    try { await running.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
    Check(socket.Messages.SequenceEqual(new[] { "greeting", "result", "status" }), "Frames reordered");
    Check(socket.MaxConcurrentSends == 1, "Overlapping WebSocket sends");
    Console.WriteLine("PASS ordered greeting/result/status, one sender, bounded shutdown");
}

// Overflow must disconnect the stalled client, not drop a result silently.
using (var socket = new ProbeSocket())
using (var client = new WebSocketClient(socket))
{
    for (int i = 0; i < 9; i++) client.Enqueue(new byte[1]);
    Check(socket.State == WebSocketState.Aborted, "Slow client not disconnected");
    Console.WriteLine("PASS bounded queue disconnects slow client");
}

// A stalled send must time out independently of another client's delivery.
using (var stalled = new ProbeSocket { StallSend = true })
using (var healthy = new ProbeSocket())
using (var slow = new WebSocketClient(stalled))
using (var fast = new WebSocketClient(healthy))
using (var stop = new CancellationTokenSource())
{
    slow.Enqueue(new byte[1]);
    var slowTask = slow.RunAsync(stop.Token);
    var fastTask = fast.RunAsync(stop.Token);
    for (int i = 0; i < 3; i++) fast.Enqueue(new byte[1]);
    await healthy.ThreeSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
    try { await slowTask.WaitAsync(TimeSpan.FromSeconds(12)); } catch (OperationCanceledException) { }
    Check(stalled.State == WebSocketState.Aborted, "Send timeout did not abort client");
    stop.Cancel();
    try { await fastTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
    Console.WriteLine("PASS stalled send times out while healthy client continues");
}

sealed class ProbeSocket : WebSocket
{
    private WebSocketState state = WebSocketState.Open;
    private int sends;
    public int MaxConcurrentSends;
    public bool StallSend;
    public List<string> Messages { get; } = new();
    public TaskCompletionSource ThreeSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override WebSocketState State => state;
    public override void Abort() => state = WebSocketState.Aborted;
    public override void Dispose() => state = WebSocketState.Closed;
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? description, CancellationToken cancellation) => Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? description, CancellationToken cancellation) => Task.CompletedTask;
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellation)
    {
        await Task.Delay(Timeout.Infinite, cancellation);
        throw new InvalidOperationException();
    }
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken cancellation)
    {
        var active = Interlocked.Increment(ref sends);
        MaxConcurrentSends = Math.Max(active, MaxConcurrentSends);
        try
        {
            await Task.Delay(StallSend ? Timeout.Infinite : 10, cancellation);
            Messages.Add(Encoding.UTF8.GetString(buffer));
            if (Messages.Count == 3) ThreeSent.TrySetResult();
        }
        finally { Interlocked.Decrement(ref sends); }
    }
}
