using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Broker.DependencyInjection;

/// <summary>
/// The setup methods of the broker package (section 4.7, D60, D61). <c>AsBroker</c> extends the payload builder of
/// <c>WithPayload</c> (D136), so that C# infers the event type and the payload type of a call.
/// </summary>
public static class BrokerExtensions
{
    /// <summary>
    /// Selects a broker client, with its logging wrapper, and <see cref="BrokerTransport"/> as the one transport of the
    /// broker channel for each payload type, with its logging wrapper (D129). The host is then a publisher of the broker
    /// channel (D101). It also registers the broker channel, so that a publisher host with no message type binds the
    /// options of the channel.
    /// </summary>
    /// <typeparam name="TClient">The client type, a thread-safe singleton.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseBrokerClient<TClient>(this MsgHatchBuilder builder)
        where TClient : class, IBrokerClient =>
        builder.UseBrokerClient(static services => ActivatorUtilities.CreateInstance<TClient>(services));

    /// <summary>Selects a broker client with a factory, for example for a test substitute, and the broker transport.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="factory">The factory of the client.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseBrokerClient(this MsgHatchBuilder builder, Func<IServiceProvider, IBrokerClient> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.Services.AddSingleton(services => WithLogging(services, factory(services)));
        return AddBrokerChannel(builder).UseTransport(static services => new BrokerTransport(services.GetRequiredService<IBrokerClient>()));
    }

    /// <summary>
    /// Registers the message type on the broker channel for one payload type, with
    /// <see cref="BrokerComposer{TEvent, TPayload}"/> (section 4.7). The composer gets the payload loader of
    /// <c>WithPayload</c> or <c>WithEventPayload</c> (D136, D139). The content type is <c>Broker:{name}</c>, and the
    /// envelope is <see cref="BrokerEnvelope{TPayload}"/>. With no retry configuration, a message has no expiry, because
    /// another service can need an integration event also after a long outage (D94).
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The type of the template payload.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="version">The current content version of the envelope.</param>
    /// <param name="json">The JSON type information of the envelope.</param>
    /// <param name="configure">The configuration of the old versions and the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsBroker<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, int version, JsonTypeInfo<BrokerEnvelope<TPayload>> json,
        Action<ChannelBuilder<BrokerEnvelope<TPayload>>>? configure = null)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        AddBrokerChannel(payload.Parent);
        return payload.AsChannel<BrokerEnvelope<TPayload>, BrokerComposer<TEvent, TPayload>>(
            BrokerChannel.Id, name, version, json, configure: configure);
    }

    private static MsgHatchBuilder AddBrokerChannel(MsgHatchBuilder builder) =>
        builder.AddChannelType(BrokerChannel.Descriptor, 1, BrokerJson.Default.BrokerChannelSettings, new BrokerChannelSettingsValidator());

    private static IBrokerClient WithLogging(IServiceProvider services, IBrokerClient client) =>
        services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.EnableLogging
            ? new BrokerClientLoggingWrapper(
                client,
                services.GetService<ILogger<BrokerClientLoggingWrapper>>() ?? NullLogger<BrokerClientLoggingWrapper>.Instance,
                services.GetRequiredService<TimeProvider>())
            : client;
}
