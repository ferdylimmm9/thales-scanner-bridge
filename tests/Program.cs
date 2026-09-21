using System.Net.WebSockets;
using System.Text;
using ThalesBridge;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

// Native-free capture state checks exercise callback ordering and failures.
bool automatic = true;
int captures = 0;
string? nativeError = null;
var capture = new CaptureCoordinator(value => { if (nativeError == null) automatic = value; return nativeError; }, () => { captures++; return nativeError; });
Check(capture.Capture() == "SCANNER_NOT_CONNECTED", "Disconnected capture accepted");
capture.SetReady(true);
Check(capture.Capture() == "MANUAL_MODE_REQUIRED", "Automatic mode accepted manual capture");
Check(capture.SetMode("invalid") == "INVALID_MODE", "Invalid mode accepted");
Check(capture.SetMode("manual") == null && !automatic, "Manual mode did not disable auto detection");
Check(capture.Capture() == null && captures == 1, "Manual capture failed");
Check(capture.Capture() == "SCANNER_BUSY" && captures == 1, "Duplicate capture reached SDK");
Check(capture.SetMode("automatic") == "SCANNER_BUSY", "Mode changed during scan");
capture.BeginRead(); capture.EndRead();
Check(capture.Capture() == null && captures == 2, "Repeat capture requires document placement");
capture.EndRead();
nativeError = "NATIVE_FAILURE";
Check(capture.SetMode("automatic") == nativeError && capture.Snapshot.Mode == "manual", "Failed mode change modified mode");
Check(capture.Capture() == nativeError && capture.Snapshot.Phase == "waiting_for_document", "Failed capture stayed busy");
nativeError = null;
capture.SetReady(false); capture.SetReady(true);
Check(capture.ApplyCurrentMode() == null && !automatic, "Reconnect lost selected mode");
Check(capture.SetMode("automatic") == null && automatic, "Automatic mode not restored");
Check(ScannerCommand.Parse("""{"type":"capture","requestId":"one"}""").Type == "capture", "Capture parsing failed");
foreach (var invalidCommand in new[] { "{}", "[]", """{"type":"capture"}""", """{"type":"set_capture_mode","requestId":"a","mode":"bad"}""" })
{
    bool rejected = false;
    try { ScannerCommand.Parse(invalidCommand); } catch (FormatException) { rejected = true; }
    Check(rejected, "Invalid command accepted");
}
Console.WriteLine("PASS capture modes, repeated/manual capture, busy guard, native failures and command validation");

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

// Fragmented commands are assembled once; oversized/binary input never invokes controls.
using (var socket = new ProbeSocket())
using (var stop = new CancellationTokenSource())
{
    socket.Incoming.Enqueue((Encoding.UTF8.GetBytes("{\"type\":"), WebSocketMessageType.Text, false));
    socket.Incoming.Enqueue((Encoding.UTF8.GetBytes("\"capture\"}"), WebSocketMessageType.Text, true));
    var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var client = new WebSocketClient(socket, json => { received.TrySetResult(json); return Task.CompletedTask; });
    var running = client.RunAsync(stop.Token);
    Check(await received.Task.WaitAsync(TimeSpan.FromSeconds(2)) == "{\"type\":\"capture\"}", "Fragmented command corrupted");
    stop.Cancel();
    try { await running; } catch (OperationCanceledException) { }
}
foreach (var binary in new[] { false, true })
{
    using var socket = new ProbeSocket();
    int calls = 0;
    for (int i = 0; i < (binary ? 1 : 5); i++) socket.Incoming.Enqueue((new byte[1024], binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, binary || i == 4));
    using var client = new WebSocketClient(socket, _ => { calls++; return Task.CompletedTask; });
    bool rejected = false;
    try { await client.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); }
    catch (WebSocketException) { rejected = true; }
    Check(rejected && calls == 0, "Unsafe command reached handler");
}
Console.WriteLine("PASS fragmented commands, binary rejection and bounded inbound size");

sealed class ProbeSocket : WebSocket
{
    private WebSocketState state = WebSocketState.Open;
    private int sends;
    public Queue<(byte[] Bytes, WebSocketMessageType Type, bool End)> Incoming { get; } = new();
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
        if (Incoming.TryDequeue(out var frame))
        {
            frame.Bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(frame.Bytes.Length, frame.Type, frame.End);
        }
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
