using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
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
    /// <typeparam name="TSettings">The settings type of the channel.</typeparam>
    /// <param name="descriptor">The descriptor of the channel.</param>
    /// <param name="settingsVersion">The version of the settings type.</param>
    /// <param name="settingsJson">The JSON type information of the settings type.</param>
    /// <param name="validator">The settings validator of the channel.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder AddChannelType<TSettings>(
        ChannelDescriptor<TSettings> descriptor, int settingsVersion, JsonTypeInfo<TSettings> settingsJson,
        IChannelSettingsValidator validator)
        where TSettings : ChannelSettings
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
    /// <param name="routerType">
    /// An open generic type with one type parameter <c>T</c> that implements <see cref="IMessageRouter{TEvent}"/> of <c>T</c>.
    /// </param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseRouter(Type routerType)
    {
        ArgumentNullException.ThrowIfNull(routerType);
        if (!IsOpenGenericRouter(routerType))
        {
            throw new ArgumentException("The router type is an open generic type with one type parameter T that implements IMessageRouter<T>.", nameof(routerType));
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
            (tenantServices, key) => registration.CreateRouter(tenantServices, key, Setup.OpenRouterType),
            static (tenantServices, key, inner) => new MessageRouterLoggingWrapper<TEvent>(
                inner, key, Loggers.For<MessageRouterLoggingWrapper<TEvent>>(tenantServices),
                tenantServices.GetRequiredService<TimeProvider>()));
        configure(new MessageBuilder<TEvent>(this, registration));
        return this;
    }

    /// <summary>
    /// Selects the in-memory stores, with their two parts and their logging wrappers, for a host with no database
    /// (D119). They are not durable.
    /// </summary>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseInMemoryStores()
    {
        Services.TryAddSingleton<InMemoryOutboxStore>();
        AddOutboxStore(
            static (tenantServices, key) => tenantServices.GetRequiredService<InMemoryOutboxStore>().For(key),
            nameof(UseInMemoryStores));
        return AddOutboxDispatchStore(
            static services => services.GetRequiredService<InMemoryOutboxStore>(), nameof(UseInMemoryStores));
    }

    /// <summary>Registers an outbox tenant store as a tenant service, with its logging wrapper (D127).</summary>
    /// <typeparam name="TStore">The store type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxStore<TStore>()
        where TStore : class, IOutboxStore =>
        UseOutboxStore(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TStore>(tenantServices));

    /// <summary>Registers an outbox tenant store with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the store of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxStore(Func<IServiceProvider, TenantKey, IOutboxStore> factory) =>
        AddOutboxStore(factory, nameof(UseOutboxStore));

    /// <summary>Registers the outbox dispatch store as a singleton, with its logging wrapper (D127, D130).</summary>
    /// <typeparam name="TStore">The store type.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxDispatchStore<TStore>()
        where TStore : class, IOutboxDispatchStore =>
        UseOutboxDispatchStore(static services => ActivatorUtilities.CreateInstance<TStore>(services));

    /// <summary>Registers the outbox dispatch store with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the store.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxDispatchStore(Func<IServiceProvider, IOutboxDispatchStore> factory) =>
        AddOutboxDispatchStore(factory, nameof(UseOutboxDispatchStore));

    private MsgHatchBuilder AddOutboxStore(Func<IServiceProvider, TenantKey, IOutboxStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Setup.Select(typeof(IOutboxStore), setupMethod);
        TenantServices.Add(
            Services, Setup, ServiceLifetime.Singleton, factory,
            static (tenantServices, key, inner) => new OutboxStoreLoggingWrapper(
                inner, key, Loggers.For<OutboxStoreLoggingWrapper>(tenantServices),
                tenantServices.GetRequiredService<TimeProvider>()));
        return this;
    }

    private MsgHatchBuilder AddOutboxDispatchStore(Func<IServiceProvider, IOutboxDispatchStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Setup.Select(typeof(IOutboxDispatchStore), setupMethod);
        Services.AddSingleton(services => Loggers.Wrap(
            services, factory(services),
            static (services, inner) => new OutboxDispatchStoreLoggingWrapper(
                inner, Loggers.For<OutboxDispatchStoreLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        return this;
    }

    private static bool IsOpenGenericRouter(Type routerType) =>
        routerType is { IsGenericTypeDefinition: true, IsAbstract: false }
        && routerType.GetGenericArguments() is [var eventType]
        && routerType.GetInterfaces().Contains(typeof(IMessageRouter<>).MakeGenericType(eventType));
}
