using System.Text;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.RabbitMQ;
using Wolverine.Runtime;

namespace MsgHatch.Broker.Wolverine.Tests.Integration.TestTypes;

/// <summary>
/// A started Wolverine host on the RabbitMQ broker of <see cref="RabbitMqBroker"/>, with the required setup of section
/// 4.7: an inline route for <see cref="TestPayload"/>, the sync retry block, and publisher confirms with tracking. It
/// declares no queue and no exchange on the broker. A second connection of the test declares the exclusive queues and
/// reads them, so the broker deletes the queues when the host closes that connection.
/// </summary>
public sealed class RabbitMqTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly IConnection _connection;

    private RabbitMqTestHost(IHost host, IConnection connection)
    {
        _host = host;
        _connection = connection;
        Client = new WolverineBrokerClient(() => new MessageBus(host.Services.GetRequiredService<IWolverineRuntime>()));
    }

    public WolverineBrokerClient Client { get; }

    public static Task<RabbitMqTestHost> StartWithQueueAsync(string queue, CancellationToken ct) =>
        StartAsync(publish => publish.ToRabbitQueue(queue), ct);

    public static Task<RabbitMqTestHost> StartWithExchangeAsync(string exchange, CancellationToken ct) =>
        StartAsync(publish => publish.ToRabbitExchange(exchange), ct);

    public async Task DeclareExclusiveQueueAsync(string queue, CancellationToken ct)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
        await channel.QueueDeclareAsync(queue, durable: false, exclusive: true, autoDelete: true, cancellationToken: ct);
    }

    public async Task<string?> GetHeaderAsync(string queue, string header, CancellationToken ct)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
        var message = await channel.BasicGetAsync(queue, autoAck: true, ct);
        return message?.BasicProperties.Headers?.TryGetValue(header, out var value) == true && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;
    }

    public async Task<bool> QueueExistsAsync(string queue, CancellationToken ct)
    {
        await using var channel = await _connection.CreateChannelAsync(cancellationToken: ct);
        try
        {
            await channel.QueueDeclarePassiveAsync(queue, ct);
            return true;
        }
        catch (OperationInterruptedException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _connection.CloseAsync();
        await _connection.DisposeAsync();
    }

    private static async Task<RabbitMqTestHost> StartAsync(
        Func<IPublishToExpression, RabbitMqSubscriberConfiguration> route, CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            options.Durability.UseSyncRetryBlock = true;
            options.UseRabbitMq(RabbitMqBroker.Uri)
                .DisableSystemRequestReplyQueueDeclaration()
                .UseSenderConnectionOnly()
                .ConfigureChannelCreation(static channel =>
                {
                    channel.PublisherConfirmationsEnabled = true;
                    channel.PublisherConfirmationTrackingEnabled = true;
                });
            route(options.PublishMessage<TestPayload>()).SendInline();
        });
        var host = builder.Build();
        await host.StartAsync(ct);
        var connection = await new ConnectionFactory { Uri = RabbitMqBroker.Uri }.CreateConnectionAsync(ct);
        return new RabbitMqTestHost(host, connection);
    }
}
