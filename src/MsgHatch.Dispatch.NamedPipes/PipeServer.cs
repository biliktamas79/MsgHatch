using System.Collections.Concurrent;
using System.IO.Pipes;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>
/// Serves the pipe of one topic (D192): one server instance waits for the next connection, and each connection of
/// another signal instance calls the notification of the topic for each read. A connection that starts with the id of
/// the own signal instance is closed, so a process never wakes itself through its own pipe.
/// </summary>
internal sealed class PipeServer : IDisposable
{
    private const int _instanceIdLength = 16;

    private readonly string _pipeName;
    private readonly Guid _owner;
    private readonly Func<ValueTask> _notify;
    private readonly Func<Exception, ValueTask> _fail;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<NamedPipeServerStream, byte> _streams = new();
    private volatile bool _failed;

    private PipeServer(string pipeName, Guid owner, Func<ValueTask> notify, Func<Exception, ValueTask> fail) =>
        (_pipeName, _owner, _notify, _fail) = (pipeName, owner, notify, fail);

    /// <summary>True after the pipe stopped with an error.</summary>
    public bool Failed => _failed;

    /// <summary>
    /// Starts to serve the pipe. It throws when the first server instance cannot start, for example when another
    /// process holds the name.
    /// </summary>
    public static PipeServer Start(string pipeName, Guid owner, Func<ValueTask> notify, Func<Exception, ValueTask> fail)
    {
        var server = new PipeServer(pipeName, owner, notify, fail);
        var first = server.NewInstance();
        _ = server.AcceptAsync(first);
        return server;
    }

    public void Dispose()
    {
        _stop.Cancel();
        foreach (var stream in _streams.Keys)
        {
            stream.Dispose();
        }
    }

    private NamedPipeServerStream NewInstance()
    {
        var stream = new NamedPipeServerStream(
            _pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _streams[stream] = 0;
        if (_stop.IsCancellationRequested)
        {
            stream.Dispose();
            _stop.Token.ThrowIfCancellationRequested();
        }

        return stream;
    }

    private async Task AcceptAsync(NamedPipeServerStream listening)
    {
        try
        {
            while (true)
            {
                await listening.WaitForConnectionAsync(_stop.Token);
                _ = ReadAsync(listening);
                listening = NewInstance();
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _failed = true;
            await _fail(exception);
        }
    }

    private async Task ReadAsync(NamedPipeServerStream connection)
    {
        try
        {
            var id = new byte[_instanceIdLength];
            await connection.ReadExactlyAsync(id, _stop.Token);
            if (new Guid(id) == _owner)
            {
                return;
            }

            var buffer = new byte[64];
            while (await connection.ReadAsync(buffer, _stop.Token) > 0)
            {
                await _notify();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            _streams.TryRemove(connection, out _);
            await connection.DisposeAsync();
        }
    }
}
