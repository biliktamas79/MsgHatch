using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// A TCP forwarder on the loopback address between the clients of a test and a server, so that a test can drop the
/// connections of the clients as a lost network does. It counts the connections, and the bytes that the clients send.
/// The integration tests of the dispatch signals link this file.
/// </summary>
internal sealed class TcpForwarder : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentDictionary<TcpClient, byte> _sockets = new();
    private readonly string _host;
    private readonly int _port;
    private int _connections;
    private long _bytesFromTheClients;

    public TcpForwarder(string host, int port)
    {
        (_host, _port) = (host, port);
        _listener.Start();
        _ = ForwardEachConnectionAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections => Volatile.Read(ref _connections);

    public long BytesFromTheClients => Interlocked.Read(ref _bytesFromTheClients);

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
                var client = await _listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref _connections);
                _ = ForwardAsync(client);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    private async Task ForwardAsync(TcpClient client)
    {
        var server = new TcpClient();
        _sockets[client] = 0;
        _sockets[server] = 0;
        try
        {
            await server.ConnectAsync(_host, _port);
            await Task.WhenAny(
                CopyAndCountAsync(client.GetStream(), server.GetStream()),
                server.GetStream().CopyToAsync(client.GetStream()));
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            _sockets.TryRemove(client, out _);
            _sockets.TryRemove(server, out _);
            client.Dispose();
            server.Dispose();
        }
    }

    private async Task CopyAndCountAsync(NetworkStream fromTheClient, NetworkStream toTheServer)
    {
        var buffer = new byte[8192];
        int count;
        while ((count = await fromTheClient.ReadAsync(buffer)) > 0)
        {
            Interlocked.Add(ref _bytesFromTheClients, count);
            await toTheServer.WriteAsync(buffer.AsMemory(0, count));
        }
    }
}
