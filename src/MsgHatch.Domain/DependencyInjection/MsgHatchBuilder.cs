using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
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

        AddChannelOptions(descriptor.Id);

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
    /// Registers a tenant service of a package (D126): one instance for each <see cref="TenantKey"/>, and an unkeyed
    /// registration that gives the instance of <see cref="TenantKey.None"/>. The service provider of the factory gives
    /// the key and each tenant service of the same key. The channel and router packages call it.
    /// </summary>
    /// <typeparam name="TService">The service type.</typeparam>
    /// <param name="lifetime">The lifetime of each instance.</param>
    /// <param name="factory">The factory of the service of one tenant key.</param>
    /// <param name="wrapWithLogging">The logging wrapper, which the builder adds only with <see cref="MsgHatchOptions.EnableLogging"/>.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder AddTenantService<TService>(
        ServiceLifetime lifetime,
        Func<IServiceProvider, TenantKey, TService> factory,
        Func<IServiceProvider, TenantKey, TService, TService>? wrapWithLogging = null)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        TenantServices.Add(Services, Setup, lifetime, factory, wrapWithLogging);
        return this;
    }

    /// <summary>
    /// Adds a classifier of the exceptions of the client <typeparamref name="TClient"/>, as a singleton (D141). A client
    /// can have many classifiers, for example one for each Wolverine transport of the host. It asks them in the order of
    /// their registration, before its built-in mapping, and the first result that is not null wins. A second call with
    /// the same classifier type adds nothing.
    /// </summary>
    /// <typeparam name="TClient">The client that uses the classifier, for example <c>WolverineBrokerClient</c>.</typeparam>
    /// <typeparam name="TClassifier">The classifier type.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder AddExceptionClassifier<TClient, TClassifier>()
        where TClassifier : class, IExceptionClassifier<TClient>
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier<TClient>, TClassifier>());
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
        Services.TryAddSingleton<InMemoryInboxStore>();
        Services.TryAddSingleton<InMemoryOutboxStore>();
        AddInboxStore(
            static (tenantServices, key) => tenantServices.GetRequiredService<InMemoryInboxStore>().For(key),
            nameof(UseInMemoryStores));
        AddInboxDispatchStore(static services => services.GetRequiredService<InMemoryInboxStore>(), nameof(UseInMemoryStores));
        AddOutboxStore(
            static (tenantServices, key) => tenantServices.GetRequiredService<InMemoryOutboxStore>().For(key),
            nameof(UseInMemoryStores));
        return AddOutboxDispatchStore(
            static services => services.GetRequiredService<InMemoryOutboxStore>(), nameof(UseInMemoryStores));
    }

    /// <summary>Registers an inbox tenant store as a tenant service, with its logging wrapper (D127).</summary>
    /// <typeparam name="TStore">The store type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseInboxStore<TStore>()
        where TStore : class, IInboxStore =>
        UseInboxStore(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TStore>(tenantServices));

    /// <summary>Registers an inbox tenant store with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the store of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="setupMethod">The setup method of a storage package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseInboxStore(Func<IServiceProvider, TenantKey, IInboxStore> factory, string? setupMethod = null) =>
        AddInboxStore(factory, setupMethod ?? nameof(UseInboxStore));

    /// <summary>Registers the inbox dispatch store as a singleton, with its logging wrapper (D127, D130).</summary>
    /// <typeparam name="TStore">The store type.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseInboxDispatchStore<TStore>()
        where TStore : class, IInboxDispatchStore =>
        UseInboxDispatchStore(static services => ActivatorUtilities.CreateInstance<TStore>(services));

    /// <summary>Registers the inbox dispatch store with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the store.</param>
    /// <param name="setupMethod">The setup method of a storage package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseInboxDispatchStore(Func<IServiceProvider, IInboxDispatchStore> factory, string? setupMethod = null) =>
        AddInboxDispatchStore(factory, setupMethod ?? nameof(UseInboxDispatchStore));

    /// <summary>Registers an outbox tenant store as a tenant service, with its logging wrapper (D127).</summary>
    /// <typeparam name="TStore">The store type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxStore<TStore>()
        where TStore : class, IOutboxStore =>
        UseOutboxStore(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TStore>(tenantServices));

    /// <summary>Registers an outbox tenant store with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the store of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="setupMethod">The setup method of a storage package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxStore(Func<IServiceProvider, TenantKey, IOutboxStore> factory, string? setupMethod = null) =>
        AddOutboxStore(factory, setupMethod ?? nameof(UseOutboxStore));

    /// <summary>Registers the outbox dispatch store as a singleton, with its logging wrapper (D127, D130).</summary>
    /// <typeparam name="TStore">The store type.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxDispatchStore<TStore>()
        where TStore : class, IOutboxDispatchStore =>
        UseOutboxDispatchStore(static services => ActivatorUtilities.CreateInstance<TStore>(services));

    /// <summary>Registers the outbox dispatch store with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the store.</param>
    /// <param name="setupMethod">The setup method of a storage package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseOutboxDispatchStore(Func<IServiceProvider, IOutboxDispatchStore> factory, string? setupMethod = null) =>
        AddOutboxDispatchStore(factory, setupMethod ?? nameof(UseOutboxDispatchStore));

    /// <summary>
    /// Registers the transport of one channel as a singleton, with its logging wrapper (D129). The host is then a
    /// publisher of that channel (D101). The channel packages call it.
    /// </summary>
    /// <typeparam name="TTransport">The transport type.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseTransport<TTransport>()
        where TTransport : class, ITransport =>
        UseTransport(static services => ActivatorUtilities.CreateInstance<TTransport>(services));

    /// <summary>Registers the transport of one channel with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the transport.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseTransport(Func<IServiceProvider, ITransport> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.AddSingleton(services => Loggers.Wrap(
            services, factory(services),
            static (services, inner) => new TransportLoggingWrapper(
                inner, Loggers.For<TransportLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        return this;
    }

    /// <summary>
    /// Selects another dispatch signal, for example one between processes (D102). The default is
    /// <see cref="InProcessDispatchSignal"/>. A signal package calls it.
    /// </summary>
    /// <typeparam name="TSignal">The signal type.</typeparam>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseDispatchSignal<TSignal>()
        where TSignal : class, IDispatchSignal =>
        UseDispatchSignal(static services => ActivatorUtilities.CreateInstance<TSignal>(services));

    /// <summary>Selects another dispatch signal with a factory, for example for a test substitute.</summary>
    /// <param name="factory">The factory of the signal.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder UseDispatchSignal(Func<IServiceProvider, IDispatchSignal> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Setup.Select(typeof(IDispatchSignal), nameof(UseDispatchSignal));
        Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Changes the dispatch options of the inbox processor. A value from code wins over the section <c>MsgHatch:Dispatch:Inbox</c>.</summary>
    /// <param name="configure">The change of the options.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder ConfigureInboxDispatch(Action<DispatchOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(OptionNames.Inbox, configure);
        return this;
    }

    /// <summary>Changes the dispatch options of the outbox dispatcher. A value from code wins over the section <c>MsgHatch:Dispatch:Outbox</c>.</summary>
    /// <param name="configure">The change of the options.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder ConfigureOutboxDispatch(Action<DispatchOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(OptionNames.Outbox, configure);
        return this;
    }

    /// <summary>
    /// Changes the error threshold and the check interval of the down state of one channel (D86). A value from code
    /// wins over the section <c>MsgHatch:ChannelDown:{Channel}</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="configure">The change of the options.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder ConfigureChannelDown(ChannelId channel, Action<ChannelDownOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddChannelOptions(channel);
        Services.Configure(channel.Name, configure);
        return this;
    }

    /// <summary>
    /// Changes the test delivery of one channel (D83). A value from code wins over the section
    /// <c>MsgHatch:TestDelivery:{Channel}</c>.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="configure">The change of the options.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder ConfigureTestDelivery(ChannelId channel, Action<TestDeliveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddChannelOptions(channel);
        Services.Configure(channel.Name, configure);
        return this;
    }

    /// <summary>
    /// Applies a value in code to an options class that two setup methods share, for example the options of a core
    /// data package and of its subscription data package (D59, D150). The value wins over each <c>BindConfiguration</c>
    /// of the options, in each order of the setup calls. A second setup method with a value in code for the same
    /// options class fails the startup validation, and the message names both methods (D62). The storage packages call it.
    /// </summary>
    /// <typeparam name="TOptions">The shared options class.</typeparam>
    /// <param name="setupMethod">The setup method that gives the value, for example <c>UseSqliteStores</c>.</param>
    /// <param name="configure">The value in code.</param>
    /// <returns>This builder.</returns>
    public MsgHatchBuilder ConfigureSharedOptions<TOptions>(string setupMethod, Action<TOptions> configure)
        where TOptions : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupMethod);
        ArgumentNullException.ThrowIfNull(configure);
        Setup.SharedOptionsValues.Add(new ImplementationSelection(typeof(TOptions), setupMethod));
        Services.PostConfigure(configure);
        return this;
    }

    private void AddChannelOptions(ChannelId channel)
    {
        if (!Setup.ChannelsWithOptions.Add(channel))
        {
            return;
        }

        Services.AddOptions<ChannelDownOptions>(channel.Name)
            .BindConfiguration($"{ChannelDownOptions.SectionName}:{channel.Name}")
            .ValidateOnStart();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ChannelDownOptions>, ChannelDownOptionsValidator>());
        Services.AddOptions<TestDeliveryOptions>(channel.Name)
            .BindConfiguration($"{TestDeliveryOptions.SectionName}:{channel.Name}")
            .ValidateOnStart();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TestDeliveryOptions>, TestDeliveryOptionsValidator>());
    }

    private MsgHatchBuilder AddInboxStore(Func<IServiceProvider, TenantKey, IInboxStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Setup.Select(typeof(IInboxStore), setupMethod);
        TenantServices.Add(
            Services, Setup, ServiceLifetime.Singleton, factory,
            static (tenantServices, key, inner) => new InboxStoreLoggingWrapper(
                inner, key, Loggers.For<InboxStoreLoggingWrapper>(tenantServices),
                tenantServices.GetRequiredService<TimeProvider>()));
        return this;
    }

    private MsgHatchBuilder AddInboxDispatchStore(Func<IServiceProvider, IInboxDispatchStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Setup.Select(typeof(IInboxDispatchStore), setupMethod);
        Services.AddSingleton(services => Loggers.Wrap(
            services, factory(services),
            static (services, inner) => new InboxDispatchStoreLoggingWrapper(
                inner, Loggers.For<InboxDispatchStoreLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        return this;
    }

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
