using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Dispatch.RabbitMq.DependencyInjection;
using MsgHatch.Dispatch.RabbitMq.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Unit;

/// <summary>
/// The RabbitMQ signal with no broker (D305): a listener closes each connection, so each connect fails. The tests wait for
/// the connect with real time and <see cref="TestHost.HangGuard"/>, and end the pause with the fake clock.
/// </summary>
public sealed class RabbitMqDispatchSignalUnitTests : IAsyncDisposable
{
    private const int _noConnection = 3200;

    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);
    private static readonly TimeSpan _reconnectPause = TimeSpan.FromSeconds(5);

    /// <summary>A real time in which the signal can connect to the listener many times, so a missing pause shows.</summary>
    private static readonly TimeSpan _timeForManyConnects = TimeSpan.FromMilliseconds(300);

    private readonly ClosingListener _broker = new();
    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private readonly ServiceProvider _host;

    public RabbitMqDispatchSignalUnitTests()
    {
        var services = TestHost.CreateServices(_time);
        services.AddMsgHatch().UseInMemoryStores().UseRabbitMqDispatchSignal(o => o.Uri = _broker.Uri);
        _host = services.Build();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IDispatchSignal Signal => _host.GetRequiredService<IDispatchSignal>();

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _broker.Dispose();
    }

    [Fact]
    public async Task WaitAsync_ConnectFails_ReturnsUnavailable()
    {
        // Act
        var error = await CompletedAsync(Signal.WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task NotifyAsync_AfterAFailedConnect_ReturnsUnavailable()
    {
        // Arrange
        await CompletedAsync(Signal.WaitAsync(_topic, Ct));

        // Act
        var error = await Signal.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task WaitAsync_InThePauseAfterAFailedConnect_ReturnsUnavailableAtOnceWithNoConnect()
    {
        // Arrange
        await CompletedAsync(Signal.WaitAsync(_topic, Ct));
        await Task.Delay(_timeForManyConnects, TimeProvider.System, Ct);

        // Act
        var wait = Signal.WaitAsync(_topic, Ct);

        // Assert
        Assert.True(wait.IsCompleted);
        Assert.Equal((Errors.Unavailable, 1), ((await wait)?.Type, _broker.Connections));
    }

    [Fact]
    public async Task WaitAsync_PauseAfterAFailedConnectIsOver_ConnectsAgainAndLogsOneWarningForTheOutage()
    {
        // Arrange
        await CompletedAsync(Signal.WaitAsync(_topic, Ct));

        // Act
        _time.Advance(_reconnectPause);
        await UntilAsync(() => _broker.Connections == 2);
        var error = await CompletedAsync(Signal.WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        var warning = Assert.Single(_host.LogRecords(), static record => record.Id.Id == _noConnection);
        Assert.Equal(LogLevel.Warning, warning.Level);
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
