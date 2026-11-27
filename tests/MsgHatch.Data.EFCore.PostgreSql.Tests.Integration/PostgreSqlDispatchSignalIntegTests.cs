using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>
/// The PostgreSQL dispatch signal (D334): the use cases of each signal, the notifications between signals on one
/// database, the payloads that wake no loop, and a listening connection that the server ends. PostgreSQL gives a
/// notification to each listener of the same database, so each test has its own empty database.
/// </summary>
public sealed class PostgreSqlDispatchSignalIntegTests : DispatchSignalTestsBase<PostgreSqlDispatchSignal>, IAsyncLifetime
{
    private const int _notListening = 3300;
    private const int _listensAgain = 3301;
    private const string _notify = "SELECT pg_notify('msghatch_dispatch', @payload)";

    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);
    private static readonly DispatchTopic _warmUp = DispatchTopic.ForChannel(new ChannelId("WarmUp"));
    private static readonly DispatchTopic _handled = DispatchTopic.ForChannel(new ChannelId("Handled"));
    private static readonly DispatchTopic _senderProbe = DispatchTopic.ForChannel(new ChannelId("Sender"));
    private static readonly TimeSpan _pauseBeforeTheNextListen = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _oneTry = TimeSpan.FromMilliseconds(50);

    private readonly PostgreSqlTestDatabase _database;
    private readonly FakeTimeProvider _time;
    private readonly FakeLogger<PostgreSqlDispatchSignal> _logger;
    private readonly List<PostgreSqlDispatchSignal> _otherSignals = [];

    public PostgreSqlDispatchSignalIntegTests(PostgreSqlFixture server)
        : this(server.Database($"signal{Guid.NewGuid():N}"[..22]), TestHost.NewFakeTime(), new FakeLogger<PostgreSqlDispatchSignal>())
    {
    }

    private PostgreSqlDispatchSignalIntegTests(PostgreSqlTestDatabase database, FakeTimeProvider time, FakeLogger<PostgreSqlDispatchSignal> logger)
        : base(new PostgreSqlDispatchSignal(database.ConnectionString, time, logger)) =>
        (_database, _time, _logger) = (database, time, logger);

    /// <summary>A payload that wakes no loop, and the topic that the payload names, which a wait of each row waits for.</summary>
    public static TheoryData<string, string, string> PayloadThatIsNotValidRows => new()
    {
        { "the sender of the signal", "{sender}:outbox:Test", "outbox:Test" },
        { "an empty payload", "", "" },
        { "no separator", "inbox", "inbox" },
        { "an unknown topic", "test:unknown", "unknown" },
        { "outbox: with a channel name that is not valid", "test:outbox:not valid", "outbox:not valid" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(_database.CreateEmptyAsync(Ct));

    public ValueTask DisposeAsync()
    {
        Signal.Dispose();
        foreach (var signal in _otherSignals)
        {
            signal.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("outbox:Test")]
    public async Task WaitAsync_NotificationOfAnotherSignalOnTheDatabase_CompletesTheWaitOfTheTopic(string topicName)
    {
        // Arrange
        var other = NewSignal();
        await ListensAsync(Signal);
        var topic = new DispatchTopic(topicName);
        var wait = Signal.WaitAsync(topic, Ct);

        // Act
        await other.NotifyAsync(topic, Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public async Task WaitAsync_ConnectionStringWithMultiplexing_ListensWithAConnectionWithNoMultiplexing()
    {
        // Arrange
        var signal = NewSignal($"{_database.ConnectionString};Multiplexing=true");

        // Act
        var listens = ListensAsync(signal);

        // Assert
        await listens;
    }

    [Fact]
    public async Task WaitAsync_OwnNotificationOnTheDatabase_DoesNotCompleteASecondWait()
    {
        // Arrange
        await using var observer = await TestListener.OpenAsync(_database.ConnectionString);
        await ListensAsync(Signal);
        await Signal.NotifyAsync(_topic, Ct);
        Assert.Null(await CompletedAsync(Signal.WaitAsync(_topic, Ct)));
        await observer.PayloadAsync($":{_topic.Name}");
        var second = Signal.WaitAsync(_topic, Ct).AsTask();

        // Act
        await HandledAsync();

        // Assert
        Assert.False(second.IsCompleted);
    }

    [Theory]
    [MemberData(nameof(PayloadThatIsNotValidRows))]
    public async Task WaitAsync_NotificationOfATestConnectionWithAPayloadThatIsNotValid_DoesNotCompleteTheWait(string row, string payload, string topicName)
    {
        // Arrange
        await using var observer = await TestListener.OpenAsync(_database.ConnectionString);
        await ListensAsync(Signal);
        await Signal.NotifyAsync(_senderProbe, Ct);
        var sender = (await observer.PayloadAsync($":{_senderProbe.Name}")).Split(':')[0];
        var wait = Signal.WaitAsync(new DispatchTopic(topicName), Ct).AsTask();
        await NotifyFromATestConnectionAsync(payload.Replace("{sender}", sender, StringComparison.Ordinal), Ct);

        // Act
        await HandledAsync();

        // Assert
        Assert.False(wait.IsCompleted, row);
    }

    [Fact]
    public async Task WaitAsync_ServerEndsTheListeningConnection_ReturnsUnavailableAndListensAgainAfterThePause()
    {
        // Arrange
        await ListensAsync(Signal);
        var waiting = Signal.WaitAsync(_topic, Ct);

        // Act
        var terminated = await _database.RowsAsync(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = current_database() AND query = 'LISTEN msghatch_dispatch'", Ct);

        // Assert
        Assert.Equal([["1"]], terminated);
        Assert.Equal(Errors.Unavailable, (await CompletedAsync(waiting))?.Type);
        _time.Advance(_pauseBeforeTheNextListen);
        await ListensAsync(Signal);
        Assert.Equal(
            [(_notListening, LogLevel.Warning, PostgresErrorCodes.AdminShutdown), (_listensAgain, LogLevel.Information, null)],
            [.. _logger.Collector.GetSnapshot().Select(static record => (record.Id.Id, record.Level, (record.Exception as PostgresException)?.SqlState))]);
    }

    private PostgreSqlDispatchSignal NewSignal() => NewSignal(_database.ConnectionString);

    private PostgreSqlDispatchSignal NewSignal(string connectionString)
    {
        var signal = new PostgreSqlDispatchSignal(connectionString, _time, new FakeLogger<PostgreSqlDispatchSignal>());
        _otherSignals.Add(signal);
        return signal;
    }

    private static Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

    /// <summary>
    /// Waits until the signal listens: the signal listens from its first wait, and a notification of a test connection
    /// completes a wait of the warm-up channel. A wait that returns an error, for example in the pause, gets a new wait.
    /// </summary>
    private async Task ListensAsync(PostgreSqlDispatchSignal signal)
    {
        using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, hang.Token);
        var wait = signal.WaitAsync(_warmUp, guard.Token).AsTask();
        while (true)
        {
            await NotifyFromATestConnectionAsync($"test:{_warmUp.Name}", guard.Token);
            await Task.WhenAny(wait, Task.Delay(_oneTry, TimeProvider.System, guard.Token));
            if (wait.IsCompleted)
            {
                if (await wait is null)
                {
                    return;
                }

                wait = signal.WaitAsync(_warmUp, guard.Token).AsTask();
            }
        }
    }

    /// <summary>
    /// Waits until the signal handled each notification before this call: PostgreSQL gives the notifications to a
    /// listener in the order of their commits, so the wake of the marker channel comes after them.
    /// </summary>
    private async Task HandledAsync()
    {
        var wait = Signal.WaitAsync(_handled, Ct);
        await NotifyFromATestConnectionAsync($"test:{_handled.Name}", Ct);
        Assert.Null(await CompletedAsync(wait));
    }

    private async Task NotifyFromATestConnectionAsync(string payload, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(_notify, connection);
        command.Parameters.AddWithValue("payload", payload);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>A test connection that listens on the channel of the signals, and keeps each payload that it gets.</summary>
    private sealed class TestListener : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly Channel<string> _payloads = Channel.CreateUnbounded<string>();

        private TestListener(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += (_, notification) => _payloads.Writer.TryWrite(notification.Payload);
        }

        public static async Task<TestListener> OpenAsync(string connectionString)
        {
            var listener = new TestListener(new NpgsqlConnection(connectionString));
            await listener._connection.OpenAsync(Ct);
            await using var listen = new NpgsqlCommand("LISTEN msghatch_dispatch", listener._connection);
            await listen.ExecuteNonQueryAsync(Ct);
            return listener;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        /// <summary>The first payload that ends with a text, from the payloads that the connection got or gets.</summary>
        public async Task<string> PayloadAsync(string end)
        {
            using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, hang.Token);
            while (true)
            {
                while (_payloads.Reader.TryRead(out var payload))
                {
                    if (payload.EndsWith(end, StringComparison.Ordinal))
                    {
                        return payload;
                    }
                }

                await _connection.WaitAsync(guard.Token);
            }
        }
    }
}
