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
    private Action? _beforeNextTimer;

    public DateTime UtcNow => GetUtcNow().UtcDateTime;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Exchange(ref _beforeNextTimer, null)?.Invoke();
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _waits.Writer.TryWrite(dueTime);
        return timer;
    }

    /// <summary>
    /// Holds the next timer creation: it completes <paramref name="reached"/>, and then blocks the thread that creates
    /// the timer until <paramref name="release"/> completes. So a test can do a step while the code is between two steps.
    /// </summary>
    public void HoldNextTimer(TaskCompletionSource reached, Task release) =>
        _beforeNextTimer = () =>
        {
            reached.TrySetResult();
            release.WaitAsync(TestHost.HangGuard, TimeProvider.System).GetAwaiter().GetResult();
        };

    public bool HasMoreWaits => _waits.Reader.TryPeek(out _);

    public Task<TimeSpan> NextWaitAsync() => _waits.Reader.ReadAsync().AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System);
}
