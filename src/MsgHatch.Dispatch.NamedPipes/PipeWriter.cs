using System.IO.Pipes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>
/// Writes the notifications of one topic into the pipe of the topic (D192). It keeps one connection, which starts with
/// the id of the own signal instance, and writes one byte for each notification. A queue of capacity 1 makes many
/// notifications before one write one byte. A notification that finds no server is lost, and only delays a send until
/// the next poll (D102, D190). After <see cref="_failedConnectsBeforeWarning"/> failed connects in a row, the writer
/// logs one Warning, and at the next connect one Information, so that a process of another OS user or with another
/// prefix shows in the log (D191).
/// </summary>
internal sealed class PipeWriter : IDisposable
{
    private const int _connectTimeoutMilliseconds = 100;
    private const int _failedConnectsBeforeWarning = 3;
    private static readonly ReadOnlyMemory<byte> _notification = new byte[] { 1 };

    private readonly Channel<bool> _pending =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly string _pipeName;
    private readonly string _topic;
    private readonly byte[] _ownerId;
    private readonly Func<bool> _ownerServes;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private NamedPipeClientStream? _client;
    private int _failedConnects;

    private PipeWriter(string pipeName, string topic, Guid owner, Func<bool> ownerServes, ILogger logger) =>
        (_pipeName, _topic, _ownerId, _ownerServes, _logger) = (pipeName, topic, owner.ToByteArray(), ownerServes, logger);

    public static PipeWriter Start(string pipeName, string topic, Guid owner, Func<bool> ownerServes, ILogger logger)
    {
        var writer = new PipeWriter(pipeName, topic, owner, ownerServes, logger);
        _ = writer.WriteAllAsync();
        return writer;
    }

    public void Notify() => _pending.Writer.TryWrite(true);

    public void Dispose()
    {
        _stop.Cancel();
        _pending.Writer.TryComplete();
        Interlocked.Exchange(ref _client, null)?.Dispose();
    }

    private async Task WriteAllAsync()
    {
        try
        {
            await foreach (var _ in _pending.Reader.ReadAllAsync(_stop.Token))
            {
                if (!_ownerServes())
                {
                    await WriteWithOneReconnectAsync();
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task WriteWithOneReconnectAsync()
    {
        for (var connection = 0; connection < 2; connection++)
        {
            _client ??= CountConnect(await ConnectOrNullAsync());
            if (_client is null || await TryWriteAsync(_client))
            {
                return;
            }

            await _client.DisposeAsync();
            _client = null;
        }
    }

    private async Task<NamedPipeClientStream?> ConnectOrNullAsync()
    {
        var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(_connectTimeoutMilliseconds, _stop.Token);
            await client.WriteAsync(_ownerId, _stop.Token);
            return client;
        }
        catch (Exception exception)
        {
            await client.DisposeAsync();
            if (exception is OperationCanceledException)
            {
                throw;
            }

            return null;
        }
    }

    private NamedPipeClientStream? CountConnect(NamedPipeClientStream? client)
    {
        if (client is null)
        {
            if (++_failedConnects == _failedConnectsBeforeWarning)
            {
                _logger.PipeNotReached(_topic, _failedConnects);
            }

            return null;
        }

        if (_failedConnects >= _failedConnectsBeforeWarning)
        {
            _logger.PipeReachedAgain(_topic);
        }

        _failedConnects = 0;
        return client;
    }

    private async Task<bool> TryWriteAsync(NamedPipeClientStream client)
    {
        try
        {
            await client.WriteAsync(_notification, _stop.Token);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return false;
        }
    }
}
