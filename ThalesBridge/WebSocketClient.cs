using System.Net.WebSockets;
using System.Threading.Channels;
using System.Text;

namespace ThalesBridge;

/// <summary>Exactly one sender per browser, with bounded buffering and send timeouts.</summary>
internal sealed class WebSocketClient(WebSocket socket, Func<string, Task>? onCommand = null) : IDisposable
{
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(8)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    });

    public void Enqueue(byte[] payload)
    {
        if (!_outgoing.Writer.TryWrite(payload))
        {
            Console.WriteLine("[bridge] disconnecting slow browser: outgoing queue full.");
            _outgoing.Writer.TryComplete();
            socket.Abort();
        }
    }

    public async Task RunAsync(CancellationToken stopping)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var send = SendLoopAsync(lifetime.Token);
        var receive = ReceiveLoopAsync(lifetime.Token);
        try { await await Task.WhenAny(send, receive); }
        finally
        {
            lifetime.Cancel();
            socket.Abort(); // Cancels outstanding I/O, including an unresponsive peer.
            try { await Task.WhenAll(send, receive); }
            catch (Exception) when (lifetime.IsCancellationRequested) { }
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellation)
    {
        await foreach (var payload in _outgoing.Reader.ReadAllAsync(cancellation))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, timeout.Token);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellation)
    {
        var buffer = new byte[1024];
        using var message = new MemoryStream();
        var utf8 = new UTF8Encoding(false, true);
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 4096)
                throw new WebSocketException("Commands must be text messages no larger than 4096 bytes.");
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            var json = utf8.GetString(message.ToArray());
            message.SetLength(0);
            if (onCommand != null) await onCommand(json);
        }
    }

    public void Dispose()
    {
        _outgoing.Writer.TryComplete();
        socket.Dispose();
    }
}
