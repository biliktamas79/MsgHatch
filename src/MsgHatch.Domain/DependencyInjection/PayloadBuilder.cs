using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The channel registrations of one payload type of a message type (D136). <c>WithPayload</c> registers the payload
/// loader one time, apart from the channels. Each composer of these channel registrations gets that loader through DI,
/// so in one DI scope all of them get the same loader instance (D135). The channel packages add their
/// <c>As&lt;Channel&gt;</c> methods to this builder, and C# infers its type arguments from it.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
public sealed class PayloadBuilder<TEvent, TPayload>
{
    private readonly MessageBuilder<TEvent> _message;

    internal PayloadBuilder(MessageBuilder<TEvent> message)
    {
        _message = message;
    }

    /// <summary>The service collection, for the channel registrations of other packages.</summary>
    public IServiceCollection Services => _message.Services;

    /// <summary>The builder of the setup, for the channel registrations of other packages, which call <c>AddChannelType</c>.</summary>
    public MsgHatchBuilder Parent => _message.Parent;

    /// <summary>
    /// Registers the message type on one channel, with a composer that gets the payload loader of this payload type
    /// (D96). The channel packages call it, after <c>AddChannelType</c>. The content type of the envelope is
    /// <c>{Channel}:{name}</c>.
    /// </summary>
    /// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
    /// <typeparam name="TComposer">The composer of the channel, a tenant service.</typeparam>
    /// <param name="channel">The channel.</param>
    /// <param name="name">The name of the registration. It contains only ASCII letters, digits, and <c>_</c>, and starts with a letter.</param>
    /// <param name="version">The current content version of the envelope.</param>
    /// <param name="json">The JSON type information of the envelope.</param>
    /// <param name="retry">The default retry options of the channel, or null for the library defaults.</param>
    /// <param name="configure">The configuration of the old versions and the retry options of this registration.</param>
    /// <returns>This builder.</returns>
    public PayloadBuilder<TEvent, TPayload> AsChannel<TEnvelope, TComposer>(
        ChannelId channel,
        string name,
        int version,
        JsonTypeInfo<TEnvelope> json,
        Action<RetryOptions>? retry = null,
        Action<ChannelBuilder<TEnvelope>>? configure = null)
        where TEnvelope : class
        where TComposer : class, IEnvelopeComposer<TEvent, TEnvelope>
    {
        _message.AsChannel<TEnvelope, TComposer>(channel, name, version, json, retry, configure);
        return this;
    }
}
