using System.Collections.Concurrent;
using System.Threading.Channels;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The default dispatch signal (D102). It holds one work queue of capacity 1 for each topic, which drops a second
/// notification, so it reaches only the loops of the same process.
/// </summary>
public sealed class InProcessDispatchSignal : IDispatchSignal
{
    private static readonly BoundedChannelOptions _oneWakeUp = new(1) { FullMode = BoundedChannelFullMode.DropWrite };

    private readonly ConcurrentDictionary<DispatchTopic, Channel<bool>> _topics = new();

    /// <inheritdoc />
    public ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct)
    {
        QueueOf(topic).Writer.TryWrite(true);
        return ValueTask.FromResult<Error?>(null);
    }

    /// <inheritdoc />
    public async ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct)
    {
        try
        {
            await QueueOf(topic).Reader.ReadAsync(ct);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
    }

    private Channel<bool> QueueOf(DispatchTopic topic) =>
        _topics.GetOrAdd(topic, static _ => Channel.CreateBounded<bool>(_oneWakeUp));
}
