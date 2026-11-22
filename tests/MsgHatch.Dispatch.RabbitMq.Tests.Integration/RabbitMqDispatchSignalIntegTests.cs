using MsgHatch.Dispatch.RabbitMq.Tests.Integration.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Integration;

/// <summary>
/// The RabbitMQ signals of two DI containers on one exchange of the broker of <see cref="RabbitMqBroker"/> (D305), and a
/// lost connection. A notification that arrives before the queue of the waiting signal is bound is lost, so a test
/// notifies again until the wait completes, with real time and <see cref="TestHost.HangGuard"/>.
/// </summary>
public sealed class RabbitMqDispatchSignalIntegTests : IAsyncDisposable
{
    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);
    private static readonly TimeSpan _reconnectPause = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _gapBetweenNotifications = TimeSpan.FromMilliseconds(100);

    private readonly SignalHosts _hosts = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _hosts.DisposeAsync();

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task WaitAsync_NotificationOfAnotherSignalOnTheExchange_CompletesTheWait()
    {
        // Arrange
        var notifier = _hosts.NewSignal();
        var wait = _hosts.NewSignal().WaitAsync(_topic, Ct);

        // Act
        var error = await NotifyUntilCompletedAsync(notifier, _topic, wait);

        // Assert
        Assert.Null(error);
    }

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task WaitAsync_InboxNotificationOfAnotherSignal_CompletesOnlyTheInboxWait()
    {
        // Arrange
        var notifier = _hosts.NewSignal();
        var signal = _hosts.NewSignal();
        var channelWait = signal.WaitAsync(_topic, Ct).AsTask();
        var inboxWait = signal.WaitAsync(DispatchTopic.Inbox, Ct);

        // Act
        var error = await NotifyUntilCompletedAsync(notifier, DispatchTopic.Inbox, inboxWait);

        // Assert
        Assert.Null(error);
        Assert.False(channelWait.IsCompleted);
    }

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task WaitAsync_ConnectionToTheBrokerCloses_ReturnsUnavailable()
    {
        // Arrange
        using var network = NetworkToTheBroker();
        var signal = await ConnectedSignalAsync(network.Uri, _hosts.NewSignal());
        var wait = signal.WaitAsync(_topic, Ct);

        // Act
        network.DropConnections();

        // Assert
        Assert.Equal(Errors.Unavailable, (await CompletedAsync(wait))?.Type);
    }

    [Fact(Skip = RabbitMqBroker.NotConfigured, SkipUnless = nameof(RabbitMqBroker.IsConfigured), SkipType = typeof(RabbitMqBroker))]
    public async Task WaitAsync_ConnectionClosedAndThePauseIsOver_CompletesForANotificationOfAnotherSignal()
    {
        // Arrange
        using var network = NetworkToTheBroker();
        var notifier = _hosts.NewSignal();
        var signal = await ConnectedSignalAsync(network.Uri, notifier);
        var lost = signal.WaitAsync(_topic, Ct);
        network.DropConnections();
        await CompletedAsync(lost);
        var inThePause = await signal.WaitAsync(_topic, Ct);

        // Act
        _hosts.Time.Advance(_reconnectPause);
        await UntilAsync(() => network.Connections == 2);
        var error = await NotifyUntilCompletedAsync(notifier, _topic, signal.WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal((Errors.Unavailable, (Error?)null), (inThePause?.Type, error));
    }

    private static TcpForwarder NetworkToTheBroker()
    {
        Assert.SkipWhen(RabbitMqBroker.Uri.Scheme == "amqps", "The forwarder of the test gives the broker another host name, which breaks TLS.");
        return new TcpForwarder(RabbitMqBroker.Uri);
    }

    /// <summary>
    /// Gives a signal of a new container that is connected to the broker: an inbox notification of another signal reached
    /// it. The tests then use the topic of a channel, so a late inbox notification changes no wait of a test.
    /// </summary>
    private async Task<IDispatchSignal> ConnectedSignalAsync(Uri broker, IDispatchSignal notifier)
    {
        var signal = _hosts.NewSignal(broker);
        Assert.Null(await NotifyUntilCompletedAsync(notifier, DispatchTopic.Inbox, signal.WaitAsync(DispatchTopic.Inbox, Ct)));
        return signal;
    }

    private static async Task<Error?> NotifyUntilCompletedAsync(IDispatchSignal notifier, DispatchTopic topic, ValueTask<Error?> wait)
    {
        var completed = wait.AsTask();
        using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, hang.Token);
        while (!completed.IsCompleted)
        {
            guard.Token.ThrowIfCancellationRequested();
            await notifier.NotifyAsync(topic, guard.Token);
            await Task.WhenAny(completed, Task.Delay(_gapBetweenNotifications, TimeProvider.System, guard.Token));
        }

        return await completed;
    }

    private static Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, hang.Token);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, guard.Token);
        }
    }
}
