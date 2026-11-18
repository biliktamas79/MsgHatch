using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Caching;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.DependencyInjection;

/// <summary>The setup methods of the subscription router package (section 4.7, D97).</summary>
public static class SubscriptionExtensions
{
    /// <summary>
    /// Selects the subscription router as the router of each message type with no router of its own (D99). Registers
    /// <see cref="ResolutionTrace"/> as the trace type <c>Trace:Subscription</c> (D98), and <see cref="ISubscriptionQueries"/>,
    /// <see cref="ISubscriptionCommands"/>, and <see cref="IRoutingVerifier"/> as scoped tenant services, each with its logging
    /// wrapper (D53, D85, D231). A message type with the subscription router needs a scope function from
    /// <see cref="WithScope"/>. With the role <see cref="MsgHatchRoles.Consumer"/>, the host needs a subscription store and a
    /// recipient directory.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptions(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        SubscriptionSetup.Of(builder.Services).UsesSubscriptions = true;
        return builder
            .UseRouter(typeof(SubscriptionRouter<>))
            .AddTraceType("Subscription", 1, SubscriptionJson.Default.ResolutionTrace)
            .AddTenantService<ISubscriptionQueries>(
                ServiceLifetime.Scoped,
                static (tenantServices, _) => ActivatorUtilities.CreateInstance<SubscriptionManager>(tenantServices),
                SubscriptionLoggers.Wrap)
            .AddTenantService<ISubscriptionCommands>(
                ServiceLifetime.Scoped,
                static (tenantServices, _) => ActivatorUtilities.CreateInstance<SubscriptionManager>(tenantServices),
                SubscriptionLoggers.Wrap)
            .AddTenantService<IRoutingVerifier>(
                ServiceLifetime.Scoped,
                static (tenantServices, _) => ActivatorUtilities.CreateInstance<RoutingVerifier>(tenantServices),
                SubscriptionLoggers.Wrap);
    }

    /// <summary>Registers the scope function of a message type, which the subscription router needs (D16).</summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="message">The builder of the message type.</param>
    /// <param name="scope">
    /// The scope of an event, for example <c>e =&gt; NotificationScope.ForUsers(e.CustomerIds)</c>. It must not throw.
    /// </param>
    /// <returns>The builder of the message type.</returns>
    public static MessageBuilder<TEvent> WithScope<TEvent>(this MessageBuilder<TEvent> message, Func<TEvent, NotificationScope> scope)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(scope);
        SubscriptionMessage<TEvent>.Of(message.Services).Scope = scope;
        return message;
    }

    /// <summary>
    /// Registers one test case of a message type for the routing verification (section 4.18). Several test cases give the
    /// variations of the test messages, for example a long name, accented characters, or optional fields that are null.
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="message">The builder of the message type.</param>
    /// <param name="testCase">The name of the test case, for example <c>basic</c>. It is in each row of the report.</param>
    /// <param name="create">
    /// Makes the test event for one test user, so that the payload loader loads the real data of that user. It must not throw.
    /// </param>
    /// <returns>The builder of the message type.</returns>
    public static MessageBuilder<TEvent> WithTestEvent<TEvent>(this MessageBuilder<TEvent> message, string testCase, Func<UserId, TEvent> create)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(testCase);
        ArgumentNullException.ThrowIfNull(create);
        SubscriptionMessage<TEvent>.Of(message.Services).TestCases.Add((testCase, create));
        return message;
    }

    /// <summary>
    /// Registers a subscription store as a singleton tenant service, with its logging wrapper (D116, D126). A host calls
    /// it only for its own store.
    /// </summary>
    /// <typeparam name="TStore">The store type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionStore<TStore>(this MsgHatchBuilder builder)
        where TStore : class, ISubscriptionStore =>
        builder.UseSubscriptionStore(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TStore>(tenantServices));

    /// <summary>
    /// Registers a subscription store with a factory, for example for a test substitute. The store packages call it. It
    /// also registers <see cref="ChannelSettingsJson"/>, which a store uses for the channel settings (D149).
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="factory">The factory of the store of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="setupMethod">The setup method of a store package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionStore(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, ISubscriptionStore> factory, string? setupMethod = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        var setup = SubscriptionSetup.Of(builder.Services);
        setup.StoreSelections.Add(setupMethod ?? nameof(UseSubscriptionStore));
        builder.Services.TryAddSingleton<ChannelSettingsJson>();
        return builder.AddTenantService(
            ServiceLifetime.Singleton,
            (tenantServices, key) => WithTheCacheOfTheSetup(setup, tenantServices, key, factory(tenantServices, key)),
            SubscriptionLoggers.Wrap);
    }

    /// <summary>
    /// Selects a cache backend, and turns on the subscription cache: <see cref="ISubscriptionCache"/> as a singleton tenant
    /// service, and the caching wrapper between the logging wrapper and the subscription store (section 4.15, D38, D40).
    /// The options bind from the section <c>MsgHatch:SubscriptionCache</c>, and a value from code wins. The cache packages
    /// call it. A host calls it only for its own backend.
    /// </summary>
    /// <typeparam name="TBackend">The backend type, a thread-safe singleton for all tenants.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionCache<TBackend>(this MsgHatchBuilder builder, Action<SubscriptionCacheOptions>? configure = null)
        where TBackend : class, ISubscriptionCacheBackend =>
        builder.UseSubscriptionCache(static services => ActivatorUtilities.CreateInstance<TBackend>(services), configure);

    /// <summary>Selects a cache backend with a factory, for example for a test substitute. The cache packages call it.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="factory">The factory of the backend, a thread-safe singleton for all tenants.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="setupMethod">The setup method of a cache package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionCache(
        this MsgHatchBuilder builder,
        Func<IServiceProvider, ISubscriptionCacheBackend> factory,
        Action<SubscriptionCacheOptions>? configure = null,
        string? setupMethod = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        SubscriptionSetup.Of(builder.Services).CacheSelections.Add(setupMethod ?? nameof(UseSubscriptionCache));
        var options = builder.Services.AddOptions<SubscriptionCacheOptions>().BindConfiguration(SubscriptionCacheOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SubscriptionCacheOptions>, SubscriptionCacheOptionsValidator>());
        builder.Services.AddSingleton(factory);
        return builder.AddTenantService<ISubscriptionCache>(
            ServiceLifetime.Singleton,
            static (tenantServices, key) => new SubscriptionCache(
                tenantServices.GetRequiredService<ISubscriptionCacheBackend>(), SubscriptionCacheKeys.For(tenantServices, key)));
    }

    /// <summary>
    /// Selects <see cref="InMemorySubscriptionStore"/>, with its logging wrapper, for a host with no database. Each tenant
    /// key has its own instance. It is not durable.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseInMemorySubscriptionStore(this MsgHatchBuilder builder) =>
        builder.UseSubscriptionStore(static (_, _) => new InMemorySubscriptionStore(), nameof(UseInMemorySubscriptionStore));

    /// <summary>Registers the consumer's recipient directory as a scoped tenant service, with its logging wrapper (D76, D126).</summary>
    /// <typeparam name="TDirectory">The directory type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRecipientDirectory<TDirectory>(this MsgHatchBuilder builder)
        where TDirectory : class, IRecipientDirectory =>
        builder.UseRecipientDirectory(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TDirectory>(tenantServices));

    /// <summary>Registers the recipient directory with a factory, for example for a test substitute.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="factory">The factory of the directory of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRecipientDirectory(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, IRecipientDirectory> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        SubscriptionSetup.Of(builder.Services).DirectorySelections.Add(nameof(UseRecipientDirectory));
        return builder.AddTenantService(ServiceLifetime.Scoped, factory, SubscriptionLoggers.Wrap);
    }

    private static ISubscriptionStore WithTheCacheOfTheSetup(
        SubscriptionSetup setup, IServiceProvider tenantServices, TenantKey key, ISubscriptionStore store) =>
        setup.CacheSelections.Count == 0
            ? store
            : new SubscriptionStoreCachingWrapper(
                store,
                tenantServices.GetRequiredService<ISubscriptionCacheBackend>(),
                SubscriptionCacheKeys.For(tenantServices, key),
                tenantServices.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value.Expiration,
                key,
                SubscriptionLoggers.For<SubscriptionStoreCachingWrapper>(tenantServices));
}
