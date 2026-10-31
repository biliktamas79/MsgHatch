using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The fluent setup of MsgHatch (section 4.7). A method that selects an implementation starts with <c>Use</c>, and a
/// method that registers a message type starts with <c>Add</c> (D61). The builder records each selection of an
/// implementation (D62).
/// </summary>
public sealed class MsgHatchBuilder
{
    internal MsgHatchBuilder(IServiceCollection services, MsgHatchSetup setup)
    {
        Services = services;
        Setup = setup;
    }

    /// <summary>The service collection, for the setup methods of other packages.</summary>
    public IServiceCollection Services { get; }

    internal MsgHatchSetup Setup { get; }

    /// <summary>
    /// Registers a channel: its descriptor, the content type <c>Settings:{Channel}</c> of its settings type, and its
    /// settings validator (D77). A second call for the same channel id changes nothing.
    /// </summary>
    /// <param name="descriptor">The descriptor of the channel.</param>
    /// <param name="settingsVersion">The version of the settings type.</param>
    /// <param name="settingsJson">The JSON type information of the settings type.</param>
    /// <param name="validator">The settings validator of the channel.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder AddChannelType(
        ChannelDescriptor descriptor, int settingsVersion, JsonTypeInfo settingsJson, IChannelSettingsValidator validator)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(validator);
        if (Setup.Channels.Exists(channel => channel.Id == descriptor.Id))
        {
            return this;
        }

        Setup.ContentTypes.Add(
            ContentTypeRegistry.Entry($"Settings:{descriptor.Id.Name}", settingsVersion, descriptor.SettingsType, settingsJson));
        Setup.Channels.Add(descriptor);
        Services.AddSingleton(validator);
        return this;
    }

    /// <summary>Registers a route trace type with the content type <c>Trace:{name}</c> (D98).</summary>
    /// <typeparam name="TTrace">The trace type.</typeparam>
    /// <param name="name">The name. It contains only ASCII letters, digits, and <c>_</c>, and starts with a letter.</param>
    /// <param name="version">The version of the trace type.</param>
    /// <param name="json">The JSON type information of the trace type.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder AddTraceType<TTrace>(string name, int version, JsonTypeInfo<TTrace> json)
        where TTrace : RouteTrace
    {
        IdentityNames.Validate(name, nameof(name));
        Setup.ContentTypes.Add(ContentTypeRegistry.Entry($"Trace:{name}", version, typeof(TTrace), json));
        return this;
    }

    /// <summary>
    /// Selects an open generic router, for example <c>typeof(SubscriptionRouter&lt;&gt;)</c>, for each message type
    /// with no router of its own (D99).
    /// </summary>
    /// <param name="routerType">An open generic type with one type parameter that implements <see cref="IMessageRouter{TEvent}"/>.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseRouter(Type routerType)
    {
        ArgumentNullException.ThrowIfNull(routerType);
        if (!IsOpenGenericRouter(routerType))
        {
            throw new ArgumentException("The router type is an open generic type with one type parameter that implements IMessageRouter<TEvent>.", nameof(routerType));
        }

        Setup.Select(typeof(IMessageRouter<>), nameof(UseRouter));
        Setup.OpenRouterType = routerType;
        return this;
    }

    /// <summary>Registers one message type.</summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="name">The message type name. It contains only ASCII letters, digits, and <c>_</c>, and starts with a letter.</param>
    /// <param name="version">The current content version of the domain event.</param>
    /// <param name="json">The JSON type information of the domain event.</param>
    /// <param name="configure">The configuration of the router, the old versions, and the channel registrations.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder AddMessage<TEvent>(
        string name, int version, JsonTypeInfo<TEvent> json, Action<MessageBuilder<TEvent>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var registration = new MessageRegistration<TEvent>(new MessageType(name), version);
        Setup.ContentTypes.Add(ContentTypeRegistry.Entry(name, version, typeof(ReceivedMessage<TEvent>), json));
        Setup.Messages.Add(registration);
        Services.AddSingleton(registration);
        TenantServices.Add<IMessageRouter<TEvent>>(
            Services, Setup, ServiceLifetime.Scoped,
            (tenantServices, key) => registration.CreateRouter(tenantServices, key, Setup.OpenRouterType));
        configure(new MessageBuilder<TEvent>(this, registration));
        return this;
    }

    private static bool IsOpenGenericRouter(Type routerType) =>
        routerType is { IsGenericTypeDefinition: true, IsAbstract: false }
        && routerType.GetGenericArguments().Length == 1
        && routerType.GetInterfaces().Any(static type =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IMessageRouter<>));
}
