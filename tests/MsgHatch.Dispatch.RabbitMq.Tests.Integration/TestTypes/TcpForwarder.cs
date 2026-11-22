using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Integration.TestTypes;

/// <summary>
/// A TCP forwarder on the loopback address between a signal and the broker, so that a test can drop the connections of
/// the signal as a lost network does. It counts the connections.
/// </summary>
public sealed class TcpForwarder : IDisposable
{
    private const int _defaultAmqpPort = 5672;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentDictionary<TcpClient, byte> _sockets = new();
    private readonly Uri _broker;
    private int _connections;

    public TcpForwarder(Uri broker)
    {
        _broker = broker;
        _listener.Start();
        _ = ForwardEachConnectionAsync();
    }

    /// <summary>The URI of the broker with the address of the forwarder.</summary>
    public Uri Uri => new UriBuilder(_broker) { Host = "127.0.0.1", Port = ((IPEndPoint)_listener.LocalEndpoint).Port }.Uri;

    public int Connections => Volatile.Read(ref _connections);

    public void DropConnections()
    {
        foreach (var socket in _sockets.Keys)
        {
            socket.Dispose();
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        DropConnections();
    }

    private async Task ForwardEachConnectionAsync()
    {
        try
        {
            while (true)
            {
                var signal = await _listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref _connections);
                _ = ForwardAsync(signal);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ForwardAsync(TcpClient signal)
    {
        var broker = new TcpClient();
        _sockets[signal] = 0;
        _sockets[broker] = 0;
        try
        {
            await broker.ConnectAsync(_broker.Host, _broker.Port > 0 ? _broker.Port : _defaultAmqpPort);
            await Task.WhenAny(
                signal.GetStream().CopyToAsync(broker.GetStream()),
                broker.GetStream().CopyToAsync(signal.GetStream()));
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            _sockets.TryRemove(signal, out _);
            _sockets.TryRemove(broker, out _);
            signal.Dispose();
            broker.Dispose();
        }
    }
}
