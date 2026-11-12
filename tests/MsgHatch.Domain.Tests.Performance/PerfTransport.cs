using System.Threading.Tasks.Sources;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// The transport of the benchmark channel. It sends nothing and allocates nothing. A benchmark of a dispatch loop
/// waits with <see cref="WaitForSendsAsync"/> until the loop sent a number of messages, on a reusable value-task
/// source, so the wait allocates nothing either.
/// </summary>
public sealed class PerfTransport : ITransport, IValueTaskSource
{
    private static readonly ValueTask<ErrorOr<PublishReceipt>> _receipt = ValueTask.FromResult(ErrorOr<PublishReceipt>.ForValue(new PublishReceipt(null)));

    private ManualResetValueTaskSourceCore<bool> _sends = new() { RunContinuationsAsynchronously = true };
    private int _remaining = int.MaxValue;

    public ChannelId Channel => PerfChannel.Id;

    /// <summary>Gives a task that completes when the transport has sent <paramref name="count"/> more messages.</summary>
    public ValueTask WaitForSendsAsync(int count)
    {
        _sends.Reset();
        Volatile.Write(ref _remaining, count);
        return new ValueTask(this, _sends.Version);
    }

    public ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct)
    {
        if (Interlocked.Decrement(ref _remaining) == 0)
        {
            _sends.SetResult(true);
        }

        return _receipt;
    }

    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);

    void IValueTaskSource.GetResult(short token) => _sends.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _sends.GetStatus(token);

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _sends.OnCompleted(continuation, state, token, flags);
}
