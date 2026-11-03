using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.SignalR.DependencyInjection;

/// <summary>
/// The setup methods of the SignalR package (section 4.7, D60, D61). <c>AsSignalR</c> extends the payload builder of
/// <c>WithPayload</c> (D136), so that C# infers the event type and the payload type of a call.
/// </summary>
public static class SignalRExtensions
{
    /// <summary>
    /// Selects <see cref="SignalRTransport{THub}"/> as the one transport of the SignalR channel for each payload type,
    /// with its logging wrapper, and the SignalR server services with <c>AddSignalR</c> (D129). The host is then a
    /// publisher of the SignalR channel (D101). It also registers the SignalR channel, so that a publisher host with no
    /// message type binds the options of the channel.
    /// </summary>
    /// <typeparam name="THub">The hub of the host.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSignalR<THub>(this MsgHatchBuilder builder)
        where THub : Hub
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSignalR();
        return AddSignalRChannel(builder).UseTransport<SignalRTransport<THub>>();
    }

    /// <summary>
    /// Registers the message type on the SignalR channel for one payload type, with
    /// <see cref="SignalRComposer{TEvent, TPayload}"/> (section 4.7). The composer gets the payload loader of
    /// <c>WithPayload</c> (D136). The content type is <c>SignalR:{name}</c>, and the envelope is
    /// <see cref="SignalREnvelope{TPayload}"/>. With no retry configuration, a message has 3 tries and expires after
    /// 5 minutes, because an old UI update has no value (D94).
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The type of the template payload.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="version">The current content version of the envelope.</param>
    /// <param name="json">The JSON type information of the envelope.</param>
    /// <param name="configure">The configuration of the old versions and the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsSignalR<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, int version, JsonTypeInfo<SignalREnvelope<TPayload>> json,
        Action<ChannelBuilder<SignalREnvelope<TPayload>>>? configure = null)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);
        AddSignalRChannel(payload.Parent);
        return payload.AsChannel<SignalREnvelope<TPayload>, SignalRComposer<TEvent, TPayload>>(
            SignalRChannel.Id, name, version, json, SetSignalRRetryDefaults, configure);
    }

    private static MsgHatchBuilder AddSignalRChannel(MsgHatchBuilder builder) =>
        builder.AddChannelType(SignalRChannel.Descriptor, 1, SignalRJson.Default.SignalRChannelSettings, new SignalRChannelSettingsValidator());

    private static void SetSignalRRetryDefaults(RetryOptions options)
    {
        options.MaxTryCount = 3;
        options.Expiry = TimeSpan.FromMinutes(5);
    }
}
