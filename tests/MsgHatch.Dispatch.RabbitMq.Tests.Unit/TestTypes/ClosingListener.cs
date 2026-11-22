using System.Net;
using System.Net.Sockets;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Unit.TestTypes;

/// <summary>
/// A TCP listener on the loopback address in place of a broker. It takes each connection and closes it at once, so each
/// connect of the signal fails in the AMQP handshake. It counts the connections.
/// </summary>
public sealed class ClosingListener : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private int _connections;

    public ClosingListener()
    {
        _listener.Start();
        _ = CloseEachConnectionAsync();
    }

    public Uri Uri => new($"amqp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");

    public int Connections => Volatile.Read(ref _connections);

    public void Dispose() => _listener.Dispose();

    private async Task CloseEachConnectionAsync()
    {
        try
        {
            while (true)
            {
                using var connection = await _listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref _connections);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }
}
