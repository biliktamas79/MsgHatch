using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Dispatch.NamedPipes.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.NamedPipes.Tests.Unit;

/// <summary>
/// The named pipe signal (D188, D192): the use cases of each signal, and the signals of two DI containers with one
/// prefix. The tests wait for the pipe with real time and <see cref="TestHost.HangGuard"/>.
/// </summary>
public sealed class NamedPipeDispatchSignalUnitTests : DispatchSignalTestsBase<IDispatchSignal>, IAsyncDisposable
{
    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);

    private readonly SignalHosts _hosts;

    public NamedPipeDispatchSignalUnitTests()
        : this(new SignalHosts())
    {
    }

    private NamedPipeDispatchSignalUnitTests(SignalHosts hosts)
        : base(hosts.NewSignal()) => _hosts = hosts;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _hosts.DisposeAsync();

    [Fact]
    public async Task WaitAsync_NotificationOfAnotherContainerWithTheSamePrefix_CompletesTheWait()
    {
        // Arrange
        var other = _hosts.NewSignal();
        var wait = Signal.WaitAsync(_topic, Ct);

        // Act
        await other.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public async Task WaitAsync_InboxNotificationOfAnotherContainer_CompletesOnlyTheInboxWait()
    {
        // Arrange
        var other = _hosts.NewSignal();
        var channelWait = Signal.WaitAsync(_topic, Ct).AsTask();
        var inboxWait = Signal.WaitAsync(DispatchTopic.Inbox, Ct);

        // Act
        await other.NotifyAsync(DispatchTopic.Inbox, Ct);

        // Assert
        Assert.Null(await CompletedAsync(inboxWait));
        Assert.False(channelWait.IsCompleted);
    }

    [Fact]
    public async Task NotifyAsync_NoContainerServesTheTopic_ReturnsAtOnceAndANotificationAfterTheWaitStartsCompletesIt()
    {
        // Arrange
        var other = _hosts.NewSignal();
        var early = other.NotifyAsync(_topic, Ct);
        var earlyCompletedAtOnce = early.IsCompleted;
        var earlyError = await early;
        var wait = Signal.WaitAsync(_topic, Ct);

        // Act
        await other.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Equal((true, (Error?)null), (earlyCompletedAtOnce, earlyError));
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public async Task WaitAsync_WaitingContainerReplacedByANewOne_TheNotificationOfAnotherContainerCompletesTheWaitOfTheNewOne()
    {
        // Arrange
        var notifier = _hosts.NewSignal();
        var replaced = _hosts.NewHost();
        var firstWait = replaced.GetRequiredService<IDispatchSignal>().WaitAsync(_topic, Ct);
        await notifier.NotifyAsync(_topic, Ct);
        await CompletedAsync(firstWait);
        await replaced.DisposeAsync();
        var wait = Signal.WaitAsync(_topic, Ct);

        // Act
        await notifier.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public async Task WaitAsync_PipeNameThatAnotherServerHolds_ReturnsUnavailableAndLogsOneWarningUntilTheNameIsFree()
    {
        // Arrange
        var host = _hosts.NewHost();
        var signal = host.GetRequiredService<IDispatchSignal>();
        var hold = _hosts.HoldPipeName(_topic);
        Error?[] whileHeld = [await signal.WaitAsync(_topic, Ct), await signal.WaitAsync(_topic, Ct)];
        hold.Dispose();
        var wait = signal.WaitAsync(_topic, Ct);

        // Act
        await _hosts.NewSignal().NotifyAsync(_topic, Ct);

        // Assert
        Assert.Equal([Errors.Unavailable, Errors.Unavailable], whileHeld.Select(static error => error?.Type));
        Assert.Null(await CompletedAsync(wait));
        var warning = Assert.Single(host.LogRecords(), static record => record.Id.Id == 3100);
        Assert.True(warning.ContainsText(_topic.Name));
    }

    private static Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);
}
