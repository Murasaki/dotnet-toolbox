using System.Threading.Channels;

namespace Samples.Service;

/// <summary>
/// In-process stand-in for whatever feeds the worker (message bus, file drop, etc.).
/// </summary>
public sealed class ItemQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public void Enqueue(string name)
    {
        if (!_channel.Writer.TryWrite(name))
            throw new InvalidOperationException("Queue is closed.");
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
