using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IDispatchSignal"/> (section 4.9). Each implementation, also a later signal between
/// processes, gets them with one subclass that only builds the signal (D102).
/// </summary>
/// <typeparam name="TSignal">The signal.</typeparam>
public abstract class DispatchSignalTestsBase<TSignal>(TSignal signal)
    where TSignal : class, IDispatchSignal
{
    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);

    protected TSignal Signal { get; } = signal;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NotifyAsync_NoWaitingLoop_ReturnsAtOnceWithNoError()
    {
        // Act
        var notify = Signal.NotifyAsync(_topic, Ct);

        // Assert
        Assert.True(notify.IsCompleted);
        Assert.Null(await notify);
    }

    [Fact]
    public async Task WaitAsync_NotificationBeforeTheWait_CompletesTheWait()
    {
        // Arrange
        await Signal.NotifyAsync(_topic, Ct);

        // Act
        var error = await Signal.WaitAsync(_topic, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task WaitAsync_NotificationWhileTheLoopWaits_CompletesTheWait()
    {
        // Arrange
        var wait = Signal.WaitAsync(_topic, Ct);

        // Act
        await Signal.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Null(await wait);
    }

    [Fact]
    public async Task WaitAsync_TwoNotificationsBeforeTwoWaits_CompletesOnlyTheFirstWait()
    {
        // Arrange
        await Signal.NotifyAsync(_topic, Ct);
        await Signal.NotifyAsync(_topic, Ct);
        await Signal.WaitAsync(_topic, Ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // Act
        var second = Signal.WaitAsync(_topic, stop.Token);

        // Assert
        Assert.False(second.IsCompleted);
        await stop.CancelAsync();
        Assert.Equal(Errors.OperationCancelled, (await second)?.Type);
    }

    [Fact]
    public async Task WaitAsync_NotificationOfAnotherTopic_DoesNotCompleteTheWait()
    {
        // Arrange
        await Signal.NotifyAsync(DispatchTopic.Inbox, Ct);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // Act
        var wait = Signal.WaitAsync(_topic, stop.Token);

        // Assert
        Assert.False(wait.IsCompleted);
        await stop.CancelAsync();
        Assert.Equal(Errors.OperationCancelled, (await wait)?.Type);
    }

    [Fact]
    public async Task WaitAsync_CancelledToken_ReturnsOperationCancelled()
    {
        // Arrange
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        var error = await Signal.WaitAsync(_topic, stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, error?.Type);
    }
}
