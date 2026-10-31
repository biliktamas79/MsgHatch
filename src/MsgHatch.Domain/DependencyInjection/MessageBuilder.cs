using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The setup of one message type (section 4.7).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class MessageBuilder<TEvent>
{
    private readonly MsgHatchBuilder _parent;
    private readonly MessageRegistration<TEvent> _registration;

    internal MessageBuilder(MsgHatchBuilder parent, MessageRegistration<TEvent> registration)
    {
        _parent = parent;
        _registration = registration;
    }

    /// <summary>The service collection, for the extension methods of other packages.</summary>
    public IServiceCollection Services => _parent.Services;

    /// <summary>Selects the router of this message type. It wins over the open generic router of <c>UseRouter</c> (D99).</summary>
    /// <typeparam name="TRouter">The router type, a tenant service.</typeparam>
    /// <returns>This builder.</returns>
    public MessageBuilder<TEvent> WithRouter<TRouter>()
        where TRouter : class, IMessageRouter<TEvent> =>
        WithRouter(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TRouter>(tenantServices));

    /// <summary>Selects the router of this message type with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the router of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <returns>This builder.</returns>
    public MessageBuilder<TEvent> WithRouter(Func<IServiceProvider, TenantKey, IMessageRouter<TEvent>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _parent.Setup.Select(typeof(IMessageRouter<TEvent>), nameof(WithRouter));
        _registration.RouterFactory = factory;
        return this;
    }

    /// <summary>Registers an old version of the domain event, with its upgrade to the current type (section 4.13).</summary>
    /// <typeparam name="TOld">The type of the old version.</typeparam>
    /// <param name="version">The old version.</param>
    /// <param name="json">The JSON type information of the old version.</param>
    /// <param name="upgrade">The upgrade from the old version to the current type.</param>
    /// <returns>This builder.</returns>
    public MessageBuilder<TEvent> WithUpgrade<TOld>(int version, JsonTypeInfo<TOld> json, Func<TOld, TEvent> upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        _parent.Setup.ContentTypes.Add(
            ContentTypeRegistry.Entry(_registration.Type.Name, version, typeof(ReceivedMessage<TOld>), json));
        _registration.AddUpgrade(version, old => upgrade((TOld)old));
        return this;
    }

    /// <summary>
    /// Registers the message type on one channel (D96). The channel packages call it, after <c>AddChannelType</c>.
    /// The content type of the envelope is <c>{Channel}:{name}</c>.
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
    public MessageBuilder<TEvent> AsChannel<TEnvelope, TComposer>(
        ChannelId channel,
        string name,
        int version,
        JsonTypeInfo<TEnvelope> json,
        Action<RetryOptions>? retry = null,
        Action<ChannelBuilder<TEnvelope>>? configure = null)
        where TEnvelope : class
        where TComposer : class, IEnvelopeComposer<TEvent, TEnvelope> =>
        AsChannel(
            channel, name, version, json,
            static (tenantServices, _) => ActivatorUtilities.CreateInstance<TComposer>(tenantServices),
            retry, configure);

    /// <summary>Registers the message type on one channel with a composer factory, for example for a test substitute.</summary>
    /// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
    /// <param name="channel">The channel.</param>
    /// <param name="name">The name of the registration. It contains only ASCII letters, digits, and <c>_</c>, and starts with a letter.</param>
    /// <param name="version">The current content version of the envelope.</param>
    /// <param name="json">The JSON type information of the envelope.</param>
    /// <param name="composer">The factory of the composer of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="retry">The default retry options of the channel, or null for the library defaults.</param>
    /// <param name="configure">The configuration of the old versions and the retry options of this registration.</param>
    /// <returns>This builder.</returns>
    public MessageBuilder<TEvent> AsChannel<TEnvelope>(
        ChannelId channel,
        string name,
        int version,
        JsonTypeInfo<TEnvelope> json,
        Func<IServiceProvider, TenantKey, IEnvelopeComposer<TEvent, TEnvelope>> composer,
        Action<RetryOptions>? retry = null,
        Action<ChannelBuilder<TEnvelope>>? configure = null)
        where TEnvelope : class
    {
        ArgumentNullException.ThrowIfNull(composer);
        var contentType = $"{channel.Name}:{IdentityNames.Validate(name, nameof(name))}";
        var channelBuilder = new ChannelBuilder<TEnvelope>(_parent.Setup, contentType);
        configure?.Invoke(channelBuilder);
        _parent.Setup.ContentTypes.Add(ContentTypeRegistry.Entry(contentType, version, typeof(OutgoingMessage<TEnvelope>), json));
        TenantServices.Add(Services, _parent.Setup, ServiceLifetime.Scoped, composer);
        AddRetryOptions(channel, contentType, retry, channelBuilder.Retry);
        _registration.AddChannel(new ChannelRegistration<TEvent, TEnvelope>(channel, contentType, version, channelBuilder.Upgrades));
        return this;
    }

    private void AddRetryOptions(
        ChannelId channel, string contentType, Action<RetryOptions>? channelDefaults, Action<RetryOptions>? withRetry)
    {
        var options = Services.AddOptions<RetryOptions>(contentType);
        if (channelDefaults is not null)
        {
            options.Configure(channelDefaults);
        }

        options
            .BindConfiguration($"{RetryOptions.SectionName}:{channel.Name}")
            .BindConfiguration($"{RetryOptions.SectionName}:{contentType}");
        if (withRetry is not null)
        {
            options.Configure(withRetry);
        }

        options.ValidateOnStart();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RetryOptions>, RetryOptionsValidator>());
    }
}
