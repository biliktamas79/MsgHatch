using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace MsgHatch.Dispatch.RabbitMq;

/// <summary>
/// The dispatch signal between the processes of one system on different machines, through a fanout exchange of RabbitMQ
/// (D305). The loops of the own process get each notification at once through an <see cref="InProcessDispatchSignal"/>.
/// One connection in the background publishes each notified topic to the exchange, with no persistence. It also reads
/// its own exclusive queue with a name from the server, and gives each topic of another signal instance to the local
/// signal. While the signal has no connection, <see cref="WaitAsync"/> and <see cref="NotifyAsync"/> return
/// <see cref="Errors.Unavailable"/>, so the loop uses its fallback wait (D102, D103). The signal connects again after
/// <see cref="_reconnectPause"/>.
/// </summary>
internal sealed class RabbitMqDispatchSignal(RabbitMqSignalOptions options, TimeProvider time, ILogger<RabbitMqDispatchSignal> logger)
    : IDispatchSignal, IDisposable
{
    private static readonly TimeSpan _reconnectPause = TimeSpan.FromSeconds(5);

    private readonly InProcessDispatchSignal _local = new();
    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly ConnectionFactory _factory = new() { Uri = options.Uri!, AutomaticRecoveryEnabled = false };
    private readonly ConcurrentDictionary<DispatchTopic, bool> _unpublished = new();

    private readonly Channel<bool> _publishNeeded =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task? _run;
    private CancellationTokenSource? _attempt;
    private bool _outageLogged;

    public async ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct)
    {
        await _local.NotifyAsync(topic, ct);
        _unpublished[topic] = true;
        _publishNeeded.Writer.TryWrite(true);
        return CurrentAttempt() is null ? new Error(Errors.Unavailable) : null;
    }

    public async ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct)
    {
        if (CurrentAttempt() is not { } attempt)
        {
            return new Error(Errors.Unavailable);
        }

        using var waitOrLoss = CancellationTokenSource.CreateLinkedTokenSource(ct, attempt);
        var error = await _local.WaitAsync(topic, waitOrLoss.Token);
        return error is null || ct.IsCancellationRequested ? error : new Error(Errors.Unavailable);
    }

    public void Dispose() => _stop.Cancel();

    /// <summary>
    /// The token of the connection attempt, which is cancelled when the attempt fails or its connection is lost. It is null
    /// in the pause after a failed attempt, and after the dispose. The first call starts the connection in the background.
    /// </summary>
    private CancellationToken? CurrentAttempt()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return null;
            }

            if (_run is null)
            {
                var first = StartAttempt();
                _run = Task.Run(() => RunAsync(first));
            }

            return _attempt?.Token;
        }
    }

    private CancellationTokenSource StartAttempt()
    {
        var attempt = new CancellationTokenSource();
        lock (_gate)
        {
            _attempt = attempt;
        }

        return attempt;
    }

    private async Task RunAsync(CancellationTokenSource attempt)
    {
        while (true)
        {
            var lost = await ServeUntilLostAsync();
            var pause = await StartPauseThenEndAttemptAsync(attempt, lost);
            if (!await PauseEndedAsync(pause))
            {
                return;
            }

            attempt = StartAttempt();
        }
    }

    /// <summary>Returns the exception that ended the connection, or null after the dispose.</summary>
    private async Task<Exception?> ServeUntilLostAsync()
    {
        try
        {
            await ServeAsync();
            return null;
        }
        catch (Exception exception)
        {
            return _stop.IsCancellationRequested ? null : exception;
        }
    }

    private async Task ServeAsync()
    {
        using var lost = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        ShutdownEventArgs? shutdown = null;
        AsyncEventHandler<ShutdownEventArgs> onShutdown = async (_, args) =>
        {
            shutdown = args;
            await lost.CancelAsync();
        };

        await using var connection = await _factory.CreateConnectionAsync(_stop.Token);
        connection.ConnectionShutdownAsync += onShutdown;
        await using var channel = await connection.CreateChannelAsync(cancellationToken: _stop.Token);
        channel.ChannelShutdownAsync += onShutdown;
        await ConsumeFromOwnQueueAsync(channel);
        LogConnectedAgain();
        try
        {
            await PublishEachTopicAsync(channel, lost.Token);
        }
        catch (OperationCanceledException) when (shutdown is not null)
        {
            throw new AlreadyClosedException(shutdown);
        }
    }

    private async Task ConsumeFromOwnQueueAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(options.ExchangeName, ExchangeType.Fanout, durable: false, autoDelete: true, cancellationToken: _stop.Token);
        var queue = await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true, cancellationToken: _stop.Token);
        await channel.QueueBindAsync(queue.QueueName, options.ExchangeName, string.Empty, cancellationToken: _stop.Token);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnReceivedAsync;
        await channel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer, _stop.Token);
    }

    private async Task PublishEachTopicAsync(IChannel channel, CancellationToken lost)
    {
        var properties = new BasicProperties { AppId = _instance, DeliveryMode = DeliveryModes.Transient };
        await foreach (var _ in _publishNeeded.Reader.ReadAllAsync(lost))
        {
            foreach (var topic in _unpublished.Keys)
            {
                if (_unpublished.TryRemove(topic, out var _))
                {
                    await channel.BasicPublishAsync(options.ExchangeName, string.Empty, mandatory: false, properties, Encoding.UTF8.GetBytes(topic.Name), lost);
                }
            }
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs delivery)
    {
        if (delivery.BasicProperties.AppId != _instance && TopicOf(delivery.Body.Span) is { } topic)
        {
            await _local.NotifyAsync(topic, CancellationToken.None);
        }
    }

    /// <summary>A wait that sees the end of the attempt can rely on the timer of the pause in the clock.</summary>
    private async Task<Task> StartPauseThenEndAttemptAsync(CancellationTokenSource attempt, Exception? lost)
    {
        var pause = Task.Delay(_reconnectPause, time, _stop.Token);
        lock (_gate)
        {
            _attempt = null;
        }

        await attempt.CancelAsync();
        if (lost is not null)
        {
            LogOncePerOutage(lost);
        }

        return pause;
    }

    private static async Task<bool> PauseEndedAsync(Task pause)
    {
        try
        {
            await pause;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void LogOncePerOutage(Exception exception)
    {
        if (!_outageLogged)
        {
            _outageLogged = true;
            logger.NoConnection(_reconnectPause, exception);
        }
    }

    private void LogConnectedAgain()
    {
        if (_outageLogged)
        {
            _outageLogged = false;
            logger.ConnectedAgain();
        }
    }

    /// <summary>The topic of the inbox or of a channel, else null, so a foreign message on the exchange adds no topic.</summary>
    private static DispatchTopic? TopicOf(ReadOnlySpan<byte> body)
    {
        var name = Encoding.UTF8.GetString(body);
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
}
