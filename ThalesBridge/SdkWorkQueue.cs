using System.Threading.Channels;
namespace ThalesBridge;

/// <summary>Serializes native SDK calls without blocking its callback threads.</summary>
internal sealed class SdkWorkQueue : IDisposable
{
    private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    public SdkWorkQueue() => _worker = Task.Run(async () =>
    {
        await foreach (var action in _queue.Reader.ReadAllAsync()) action();
    });
    public Task<T> RunAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new ObjectDisposedException(nameof(SdkWorkQueue)));
        return completion.Task;
    }
    public void Dispose() { _queue.Writer.TryComplete(); _worker.GetAwaiter().GetResult(); }
}
