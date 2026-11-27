using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql;

/// <summary>
/// The dispatch signal of the hosts that use one PostgreSQL database of the EF Core stores, also on different machines
/// (D334). At the first <see cref="WaitAsync"/>, one connection of its own runs <c>LISTEN msghatch_dispatch</c>, and
/// gives the topic of each notification of another signal instance to a local <see cref="InProcessDispatchSignal"/>.
/// While it does not listen, <see cref="WaitAsync"/> returns <see cref="Errors.Unavailable"/>, so the loop uses its
/// fallback wait (section 4.9), and the signal connects again after a pause of 5 seconds. The listening connection has no
/// multiplexing, because Npgsql does not wait for notifications in that mode. It has the <c>Keepalive</c> of the connection
/// string, or else 30 seconds, so Npgsql sends a message after each 30 seconds with no traffic, and a firewall that drops
/// idle connections keeps it open. A pooler in transaction mode, for example PgBouncer, does not support <c>LISTEN</c>, so
/// the connection string must reach the server or a pooler in session mode.
/// <para>
/// <see cref="NotifyAsync"/> notifies the loops of the own process at once, and a writer in the background runs
/// <c>SELECT pg_notify('msghatch_dispatch', @payload)</c> on a pooled connection, one time for each topic. The payload is
/// <c>{sender}:{topic}</c>, so the signal skips its own notifications. Many notifications before one write become one
/// notification for each topic.
/// </para>
/// </summary>
public sealed class PostgreSqlDispatchSignal : IDispatchSignal, IDisposable
{
    private const string _listen = "LISTEN msghatch_dispatch";
    private const string _notify = "SELECT pg_notify('msghatch_dispatch', @payload)";

    private const int _defaultKeepaliveSeconds = 30;

    private static readonly TimeSpan _pauseBeforeTheNextListen = TimeSpan.FromSeconds(5);

    private readonly string _connectionString;
    private readonly string _listenConnectionString;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly InProcessDispatchSignal _local = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _sender = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<DispatchTopic, bool> _topicsToWrite = new();
    private readonly Channel<bool> _writeWaits =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private Error? _lastWriteError;
    private CancellationTokenSource _listenWorks = new();
    private int _listenStarted;
    private bool _broken;

    /// <summary>Makes the signal on the database of the PostgreSQL stores. It opens no connection.</summary>
    /// <param name="connectionString">The connection string of the database of the stores.</param>
    /// <param name="time">The clock of the pause before the signal listens again.</param>
    /// <param name="logger">The logger of a connection that does not listen.</param>
    public PostgreSqlDispatchSignal(string connectionString, TimeProvider time, ILogger<PostgreSqlDispatchSignal> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        (_connectionString, _time, _logger) = (connectionString, time, logger);
        _listenConnectionString = ListenConnectionStringOf(connectionString);
        _ = WriteTheTopicsUntilDisposedAsync();
    }

    /// <summary>
    /// Notifies the loops of the own process at once, and writes the topic for the other hosts in the background.
    /// </summary>
    /// <param name="topic">The topic.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or <see cref="Errors.Unavailable"/> while the last write of a notification failed.</returns>
    public async ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct)
    {
        await _local.NotifyAsync(topic, ct);
        _topicsToWrite[topic] = true;
        _writeWaits.Writer.TryWrite(true);
        return Volatile.Read(ref _lastWriteError);
    }

    /// <inheritdoc />
    public async ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct)
    {
        StartToListenOnce();
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct, Volatile.Read(ref _listenWorks).Token);
        var error = await _local.WaitAsync(topic, wake.Token);
        return error is null || ct.IsCancellationRequested ? error : new Error(Errors.Unavailable);
    }

    /// <summary>Stops the listening connection and the writer.</summary>
    public void Dispose() => _stop.Cancel();

    private static string ListenConnectionStringOf(string connectionString)
    {
        var listen = new NpgsqlConnectionStringBuilder(connectionString) { Multiplexing = false };
        if (listen.KeepAlive == 0)
        {
            listen.KeepAlive = _defaultKeepaliveSeconds;
        }

        return listen.ConnectionString;
    }

    private async Task WriteTheTopicsUntilDisposedAsync()
    {
        try
        {
            await foreach (var _ in _writeWaits.Reader.ReadAllAsync(_stop.Token))
            {
                Volatile.Write(ref _lastWriteError, await WriteAsync([.. TakeTheTopicsToWrite()]));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private IEnumerable<DispatchTopic> TakeTheTopicsToWrite() =>
        _topicsToWrite.Keys.Where(topic => _topicsToWrite.TryRemove(topic, out _));

    private async Task<Error?> WriteAsync(DispatchTopic[] topics)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(_stop.Token);
            foreach (var topic in topics)
            {
                await using var command = new NpgsqlCommand(_notify, connection);
                command.Parameters.AddWithValue("payload", $"{_sender}:{topic.Name}");
                await command.ExecuteNonQueryAsync(_stop.Token);
            }

            return null;
        }
        catch (Exception exception) when (!_stop.IsCancellationRequested)
        {
            return new Error(Errors.Unavailable, exception.Message, exception);
        }
    }

    private void StartToListenOnce()
    {
        if (Interlocked.Exchange(ref _listenStarted, 1) == 0)
        {
            _ = ListenUntilDisposedAsync();
        }
    }

    private async Task ListenUntilDisposedAsync()
    {
        try
        {
            while (true)
            {
                await PauseAfterAsync(await ListenUntilItEndsAsync());
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    /// <returns>The exception that ended the connection.</returns>
    private async Task<Exception> ListenUntilItEndsAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_listenConnectionString);
            connection.Notification += (_, notification) => _ = NotifyTheLocalLoopsAsync(notification.Payload);
            await connection.OpenAsync(_stop.Token);
            await using (var listen = new NpgsqlCommand(_listen, connection))
            {
                await listen.ExecuteNonQueryAsync(_stop.Token);
            }

            ListensAgain();
            while (true)
            {
                await connection.WaitAsync(_stop.Token);
            }
        }
        catch (Exception exception) when (!_stop.IsCancellationRequested)
        {
            return exception;
        }
    }

    private async Task NotifyTheLocalLoopsAsync(string payload)
    {
        if (TopicOfAnotherSender(payload) is { } topic)
        {
            await _local.NotifyAsync(topic, CancellationToken.None);
        }
    }

    /// <summary>
    /// The topic of a payload <c>{sender}:{topic}</c> of another signal instance: the inbox, or a channel with a valid name.
    /// Else null, so a foreign notification on the channel adds no topic.
    /// </summary>
    private DispatchTopic? TopicOfAnotherSender(string payload)
    {
        var separator = payload.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || payload.AsSpan(0, separator).SequenceEqual(_sender))
        {
            return null;
        }

        var name = payload[(separator + 1)..];
        return name == DispatchTopic.Inbox.Name || IsTopicOfAChannel(name) ? new DispatchTopic(name) : null;
    }

    private static bool IsTopicOfAChannel(string name)
    {
        try
        {
            return DispatchTopic.ForChannel(new ChannelId(name[(name.IndexOf(':', StringComparison.Ordinal) + 1)..])).Name == name;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Starts the pause in the clock before it ends the waits, so a wait that sees the break can rely on the timer.</summary>
    private Task PauseAfterAsync(Exception exception)
    {
        var pause = Task.Delay(_pauseBeforeTheNextListen, _time, _stop.Token);
        Break(exception);
        return pause;
    }

    private void Break(Exception exception)
    {
        if (!_broken)
        {
            _broken = true;
            _logger.NotListening(_pauseBeforeTheNextListen, exception);
        }

        Volatile.Read(ref _listenWorks).Cancel();
    }

    private void ListensAgain()
    {
        if (_broken)
        {
            _broken = false;
            Volatile.Write(ref _listenWorks, new CancellationTokenSource());
            _logger.ListensAgain();
        }
    }
}
