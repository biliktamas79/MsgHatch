using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit;

/// <summary>
/// The PostgreSQL dispatch signal with no server (D334): a listener closes each connection, so each connect fails, or a
/// silent listener keeps the connect in the handshake of Npgsql. One open of Npgsql can make several TCP connections, one
/// for each SSL mode that it tries, so a test compares the count of connections with the count after the first listen.
/// The tests wait for the connect with real time and <see cref="TestHost.HangGuard"/>, and end the pause with the fake
/// clock. The integration tests cover the notifications between signals on a server.
/// </summary>
public sealed class PostgreSqlDispatchSignalUnitTests : IDisposable
{
    private const int _notListening = 3300;

    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);
    private static readonly TimeSpan _pauseBeforeTheNextListen = TimeSpan.FromSeconds(5);

    /// <summary>A real time in which the signal can connect to the listener many times, so a missing pause shows.</summary>
    private static readonly TimeSpan _timeForManyConnects = TimeSpan.FromMilliseconds(300);

    /// <summary>A real time in which a warning that the signal logs after its second failed connect shows.</summary>
    private static readonly TimeSpan _timeForALateWarning = TimeSpan.FromMilliseconds(300);

    /// <summary>A real time between two notifications, so the writer of the signal can try a write.</summary>
    private static readonly TimeSpan _timeForAWrite = TimeSpan.FromMilliseconds(20);

    private readonly ClosingListener _database = new();
    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private readonly FakeLogger<PostgreSqlDispatchSignal> _logger = new();
    private readonly PostgreSqlDispatchSignal _signal;

    public PostgreSqlDispatchSignalUnitTests() => _signal = SignalOn(_database.Port, _time, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _signal.Dispose();
        _database.Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Host=127.0.0.1;Unknown=1")]
    public void Constructor_InvalidConnectionString_ThrowsArgumentException(string? connectionString)
    {
        // Act
        var exception = Record.Exception(() => new PostgreSqlDispatchSignal(connectionString!, _time, _logger));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public async Task WaitAsync_ConnectFails_ReturnsUnavailable()
    {
        // Act
        var error = await CompletedAsync(_signal.WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task WaitAsync_InThePauseAfterAFailedConnect_ReturnsUnavailableAtOnceWithNoConnect()
    {
        // Arrange
        await CompletedAsync(_signal.WaitAsync(_topic, Ct));
        var connectsOfTheFirstListen = _database.Connections;
        await Task.Delay(_timeForManyConnects, TimeProvider.System, Ct);

        // Act
        var wait = _signal.WaitAsync(_topic, Ct);

        // Assert
        Assert.True(wait.IsCompleted);
        Assert.Equal((Errors.Unavailable, connectsOfTheFirstListen), ((await wait)?.Type, _database.Connections));
    }

    [Fact]
    public async Task WaitAsync_PauseAfterAFailedConnectIsOver_ConnectsAgainAndLogsOneWarningForTheOutage()
    {
        // Arrange
        await CompletedAsync(_signal.WaitAsync(_topic, Ct));
        var connectsOfTheFirstListen = _database.Connections;

        // Act
        _time.Advance(_pauseBeforeTheNextListen);
        await UntilAsync(() => _database.Connections > connectsOfTheFirstListen);
        var error = await CompletedAsync(_signal.WaitAsync(_topic, Ct));
        await Task.Delay(_timeForALateWarning, TimeProvider.System, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        Assert.Equal([(_notListening, LogLevel.Warning)], [.. _logger.Collector.GetSnapshot().Select(static record => (record.Id.Id, record.Level))]);
    }

    [Fact]
    public async Task WaitAsync_NotificationOfTheOwnProcessWhileItConnects_CompletesTheWaitAndNotifyReturnsNoError()
    {
        // Arrange
        using var database = new SilentListener();
        using var signal = SignalOn(database.Port, TestHost.NewFakeTime(), new FakeLogger<PostgreSqlDispatchSignal>());
        var wait = signal.WaitAsync(_topic, Ct);

        // Act
        var notify = await signal.NotifyAsync(_topic, Ct);

        // Assert
        Assert.Equal(((ErrorType?)null, (ErrorType?)null), (notify?.Type, (await CompletedAsync(wait))?.Type));
    }

    [Fact]
    public async Task WaitAsync_CallerCancelsWhileItConnects_ReturnsOperationCancelled()
    {
        // Arrange
        using var database = new SilentListener();
        using var signal = SignalOn(database.Port, TestHost.NewFakeTime(), new FakeLogger<PostgreSqlDispatchSignal>());
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var wait = signal.WaitAsync(_topic, cancel.Token);

        // Act
        await cancel.CancelAsync();

        // Assert
        Assert.Equal(Errors.OperationCancelled, (await CompletedAsync(wait))?.Type);
    }

    [Fact]
    public async Task NotifyAsync_AfterAFailedWrite_ReturnsUnavailable()
    {
        // Act
        var error = await NotifyUntilAnErrorAsync();

        // Assert
        Assert.Equal(Errors.Unavailable, error.Type);
    }

    private static PostgreSqlDispatchSignal SignalOn(int port, FakeTimeProvider time, FakeLogger<PostgreSqlDispatchSignal> logger) =>
        new($"Host=127.0.0.1;Port={port};Database=signal", time, logger);

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

    /// <summary>
    /// Notifies until a notification returns an error. The signal writes the notifications in the background, and only the
    /// result of the next notification shows that a write failed.
    /// </summary>
    private async Task<Error> NotifyUntilAnErrorAsync()
    {
        using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, hang.Token);
        Error? error;
        while ((error = await _signal.NotifyAsync(_topic, guard.Token)) is null)
        {
            await Task.Delay(_timeForAWrite, TimeProvider.System, guard.Token);
        }

        return error;
    }
}
