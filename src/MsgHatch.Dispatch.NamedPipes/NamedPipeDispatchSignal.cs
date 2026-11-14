using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>
/// The dispatch signal between the processes of one OS user on one machine (D188, D191, D192). The loops of the own
/// process get each notification at once through an <see cref="InProcessDispatchSignal"/>. The process that waits for a
/// topic serves the pipe of the topic, and another process that notifies the topic writes into that pipe. While the
/// pipe of a topic cannot be served, <see cref="WaitAsync"/> returns <see cref="Errors.Unavailable"/>, and the loop uses
/// its fallback wait (D102, D103).
/// </summary>
internal sealed class NamedPipeDispatchSignal(string pipeNamePrefix, ILogger<NamedPipeDispatchSignal> logger) : IDispatchSignal, IDisposable
{
    private readonly InProcessDispatchSignal _local = new();
    private readonly Guid _instance = Guid.NewGuid();
    private readonly Lock _gate = new();
    private readonly Dictionary<DispatchTopic, PipeServer> _servers = [];
    private readonly Dictionary<DispatchTopic, PipeWriter> _writers = [];
    private readonly HashSet<DispatchTopic> _notServed = [];
    private bool _disposed;

    public async ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct)
    {
        await _local.NotifyAsync(topic, ct);
        if (!Serves(topic))
        {
            WriterOf(topic)?.Notify();
        }

        return null;
    }

    public ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct) =>
        TryServe(topic) ? _local.WaitAsync(topic, ct) : ValueTask.FromResult<Error?>(new Error(Errors.Unavailable));

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var server in _servers.Values)
            {
                server.Dispose();
            }

            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }

            _servers.Clear();
            _writers.Clear();
        }
    }

    private bool Serves(DispatchTopic topic)
    {
        lock (_gate)
        {
            return _servers.ContainsKey(topic);
        }
    }

    private PipeWriter? WriterOf(DispatchTopic topic)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (!_writers.TryGetValue(topic, out var writer))
            {
                writer = PipeWriter.Start(PipeNames.Of(pipeNamePrefix, topic), _instance, () => Serves(topic));
                _writers[topic] = writer;
            }

            return writer;
        }
    }

    private bool TryServe(DispatchTopic topic)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_servers.TryGetValue(topic, out var server))
            {
                if (!server.Failed)
                {
                    return true;
                }

                server.Dispose();
                _servers.Remove(topic);
            }

            return TryStartServer(topic);
        }
    }

    private bool TryStartServer(DispatchTopic topic)
    {
        try
        {
            _servers[topic] = PipeServer.Start(
                PipeNames.Of(pipeNamePrefix, topic), _instance, () => NotifyLocallyAsync(topic), exception => OnServerFailedAsync(topic, exception));
            _notServed.Remove(topic);
            return true;
        }
        catch (Exception exception)
        {
            LogOncePerOutage(topic, exception);
            return false;
        }
    }

    private async ValueTask NotifyLocallyAsync(DispatchTopic topic) => await _local.NotifyAsync(topic, CancellationToken.None);

    private async ValueTask OnServerFailedAsync(DispatchTopic topic, Exception exception)
    {
        lock (_gate)
        {
            LogOncePerOutage(topic, exception);
        }

        await NotifyLocallyAsync(topic);
    }

    private void LogOncePerOutage(DispatchTopic topic, Exception exception)
    {
        if (_notServed.Add(topic))
        {
            logger.PipeNotServed(topic.Name, exception);
        }
    }
}
