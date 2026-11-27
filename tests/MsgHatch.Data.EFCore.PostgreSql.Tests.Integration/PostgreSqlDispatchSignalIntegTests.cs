using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>
/// The PostgreSQL dispatch signal (D334): the use cases of each signal, the notifications between signals on one
/// database, the payloads that wake no loop, a listening connection that the server ends, the keepalive of the listening
/// connection through a TCP forwarder in the test process, and the wake after the commit of the host. PostgreSQL gives a notification to each listener of the same database, so each test has its own empty
/// database, and the test of the host has its own database of the migrations.
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
    private static readonly TimeSpan _defaultKeepalive = TimeSpan.FromSeconds(30);

    /// <summary>A real time in which the default keepalive of 30 seconds shows, with a margin for the scheduler.</summary>
    private static readonly TimeSpan _timeForTheDefaultKeepalive = TimeSpan.FromSeconds(40);

    private readonly PostgreSqlFixture _server;
    private readonly PostgreSqlTestDatabase _database;
    private readonly FakeTimeProvider _time;
    private readonly FakeLogger<PostgreSqlDispatchSignal> _logger;
    private readonly List<PostgreSqlDispatchSignal> _otherSignals = [];

    public PostgreSqlDispatchSignalIntegTests(PostgreSqlFixture server)
        : this(server, server.Database($"signal{Guid.NewGuid():N}"[..22]), TestHost.NewFakeTime(), new FakeLogger<PostgreSqlDispatchSignal>())
    {
    }

    private PostgreSqlDispatchSignalIntegTests(
        PostgreSqlFixture server, PostgreSqlTestDatabase database, FakeTimeProvider time, FakeLogger<PostgreSqlDispatchSignal> logger)
        : base(new PostgreSqlDispatchSignal(database.ConnectionString, time, logger)) =>
        (_server, _database, _time, _logger) = (server, database, time, logger);

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
    public async Task WaitAsync_ConnectionStringWithNoKeepalive_SendsAKeepaliveAfter30SecondsWithNoTraffic()
    {
        // Arrange
        using var network = NetworkTo(_database);
        var signal = NewSignal(ThroughThe(network, _database.ConnectionString));
        await ListensAsync(signal);

        // Act
        var silence = await SilenceOfTheClientsAsync(network);

        // Assert
        Assert.InRange(silence, _defaultKeepalive - TimeSpan.FromSeconds(5), _timeForTheDefaultKeepalive);
    }

    [Fact]
    public async Task WaitAsync_ConnectionStringWithAKeepalive_SendsTheKeepaliveOfTheConnectionString()
    {
        // Arrange
        using var network = NetworkTo(_database);
        var signal = NewSignal($"{ThroughThe(network, _database.ConnectionString)};Keepalive=1");
        await ListensAsync(signal);

        // Act
        var silence = await SilenceOfTheClientsAsync(network);

        // Assert
        Assert.InRange(silence, TimeSpan.Zero, TimeSpan.FromSeconds(5));
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
        await NotifyFromATestConnectionAsync(_database.ConnectionString, payload.Replace("{sender}", sender, StringComparison.Ordinal), Ct);

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

    [Fact]
    public async Task CommitAsync_TransactionOfTheHostWithAnInboxRow_WakesTheInboxWaitOfAnotherSignal()
    {
        // Arrange
        var database = MigratedStores.OfHost(_server, "signal_host");
        await database.ResetAsync(Ct);
        await using var provider = MigratedStores.BuildWithHostContextAndTheSignal(database);
        var other = NewSignal(database.ConnectionString);
        await ListensAsync(other, database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<TestHostContext>();
        await using var transaction = await host.Database.BeginTransactionAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().AddAsync(TestMessages.AReceivedMessage(new TestEvent("text")), Ct);
        await host.SaveChangesAsync(Ct);
        var wait = other.WaitAsync(DispatchTopic.Inbox, Ct);

        // Act
        await transaction.CommitAsync(Ct);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    private PostgreSqlDispatchSignal NewSignal() => NewSignal(_database.ConnectionString);

    private PostgreSqlDispatchSignal NewSignal(string connectionString)
    {
        var signal = new PostgreSqlDispatchSignal(connectionString, _time, new FakeLogger<PostgreSqlDispatchSignal>());
        _otherSignals.Add(signal);
        return signal;
    }

    private static TcpForwarder NetworkTo(PostgreSqlTestDatabase database)
    {
        var server = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        return new TcpForwarder(server.Host!, server.Port);
    }

    private static string ThroughThe(TcpForwarder network, string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Host = "127.0.0.1", Port = network.Port }.ConnectionString;

    /// <summary>The time from the call until a client of the forwarder sends a byte, at most <see cref="_timeForTheDefaultKeepalive"/>.</summary>
    private static async Task<TimeSpan> SilenceOfTheClientsAsync(TcpForwarder network)
    {
        var bytes = network.BytesFromTheClients;
        var start = TimeProvider.System.GetTimestamp();
        using var limit = new CancellationTokenSource(_timeForTheDefaultKeepalive, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, limit.Token);
        while (network.BytesFromTheClients == bytes)
        {
            await Task.Delay(_oneTry, TimeProvider.System, guard.Token);
        }

        return TimeProvider.System.GetElapsedTime(start);
    }

    private static Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

    /// <summary>
    /// Waits until the signal listens: the signal listens from its first wait, and a notification of a test connection
    /// completes a wait of the warm-up channel. A wait that returns an error, for example in the pause, gets a new wait.
    /// </summary>
    private Task ListensAsync(PostgreSqlDispatchSignal signal) => ListensAsync(signal, _database.ConnectionString);

    private static async Task ListensAsync(PostgreSqlDispatchSignal signal, string connectionString)
    {
        using var hang = new CancellationTokenSource(TestHost.HangGuard, TimeProvider.System);
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct, hang.Token);
        var wait = signal.WaitAsync(_warmUp, guard.Token).AsTask();
        while (true)
        {
            await NotifyFromATestConnectionAsync(connectionString, $"test:{_warmUp.Name}", guard.Token);
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
        await NotifyFromATestConnectionAsync(_database.ConnectionString, $"test:{_handled.Name}", Ct);
        Assert.Null(await CompletedAsync(wait));
    }

    private static async Task NotifyFromATestConnectionAsync(string connectionString, string payload, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
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
