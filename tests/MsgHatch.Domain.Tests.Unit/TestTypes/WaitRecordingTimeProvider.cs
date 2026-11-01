using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// A fake clock that reports the due time of each timer that the code creates, so that a dispatch loop test can wait
/// until the loop waits, and can read how long it waits. It starts at the time of <see cref="TestHost.Start"/>.
/// </summary>
public sealed class WaitRecordingTimeProvider() : FakeTimeProvider(TestHost.Start)
{
    private readonly Channel<TimeSpan> _waits = Channel.CreateUnbounded<TimeSpan>();

    public DateTime UtcNow => GetUtcNow().UtcDateTime;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _waits.Writer.TryWrite(dueTime);
        return timer;
    }

    public bool HasMoreWaits => _waits.Reader.TryPeek(out _);

    public Task<TimeSpan> NextWaitAsync() => _waits.Reader.ReadAsync().AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System);
}
