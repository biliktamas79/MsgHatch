using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Telemetry;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The entry point of the MsgHatch setup.</summary>
public static class ServiceCollectionExtensions
{
    private const string _inboxHasNoExpiry =
        "Expiry: The inbox has no expiry. The age of an event in the inbox counts against the expiry of each outgoing message (D142).";

    /// <summary>
    /// Registers the core services of MsgHatch: the options, the in-memory <see cref="IMessageRegistry"/> from the
    /// registrations (D117), the content type registry, <see cref="MsgHatchJson"/>, <see cref="InProcessDispatchSignal"/>
    /// as the <see cref="IDispatchSignal"/>, the in-process <see cref="IRegistryQueries"/> as the default of
    /// <see cref="MsgHatchBuilder.UseRegistryQueries"/> (D268, D269), the in-process <see cref="IRegistryCommands"/> as the
    /// default of <see cref="MsgHatchBuilder.UseRegistryCommands"/> (D283), the in-process <see cref="IDeliveryQueries"/> and
    /// <see cref="IDeliveryCommands"/> as the defaults of <see cref="MsgHatchBuilder.UseDeliveryQueries"/> and
    /// <see cref="MsgHatchBuilder.UseDeliveryCommands"/> (D289), and <see cref="TimeProvider.System"/>
    /// if the host has registered no <see cref="TimeProvider"/>. The options bind from the root of the configuration
    /// sections of MsgHatch in the <c>IConfiguration</c> of the host, by default the section <c>MsgHatch</c> (D328). Each
    /// other section of MsgHatch is under this root. Only the first call binds the options and sets the root, and a later
    /// call reuses the setup of the first call (D314).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// An optional change of the options in code. A value from code wins over the configuration in each order of the
    /// calls, and the value of a later call wins over the value of an earlier call.
    /// </param>
    /// <param name="configSectionPath">
    /// The root of all configuration sections of MsgHatch as a full path, for example <c>Messaging</c> or
    /// <c>App:Messaging</c>, or null for <see cref="MsgHatchOptions.DefaultSectionKey"/>. A later call with null keeps the
    /// root of the first call. A later call with another root fails the startup validation (D62, D328).
    /// </param>
    /// <returns>The builder for the other setup methods.</returns>
    /// <exception cref="ArgumentException"><paramref name="configSectionPath"/> is empty or white space.</exception>
    public static MsgHatchBuilder AddMsgHatch(
        this IServiceCollection services, Action<MsgHatchOptions>? configure = null, string? configSectionPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configSectionPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configSectionPath);
        }

        var setup = ExistingSetup(services) ?? AddSetup(services, configSectionPath ?? MsgHatchOptions.DefaultSectionKey);
        setup.UseConfigSectionPath(configSectionPath);
        if (configure is not null)
        {
            services.AddOptions<MsgHatchOptions>().Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageRegistry>(_ =>
            new InMemoryMessageRegistry(setup.Channels, setup.Messages.Select(static message => message.Descriptor)));
        services.TryAddSingleton<IContentTypeRegistry>(_ => new ContentTypeRegistry(setup.ContentTypes));
        services.TryAddSingleton<MsgHatchJson>();
        services.TryAddSingleton<IDispatchSignal, InProcessDispatchSignal>();
        services.AddMetrics();
        services.TryAddSingleton<MsgHatchMetrics>();
        services.TryAddScoped<NotificationPipeline>();
        services.TryAddScoped(static services => Loggers.Wrap<INotificationService>(
            services, new NotificationService(services.GetRequiredService<NotificationPipeline>()),
            static (services, inner) => new NotificationServiceLoggingWrapper(
                inner, Loggers.For<NotificationServiceLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        services.TryAddSingleton(static services => Loggers.Wrap<IInboxProcessor>(
            services, InboxProcessor.Create(services),
            static (services, inner) => new InboxProcessorLoggingWrapper(
                inner, Loggers.For<InboxProcessorLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        services.TryAddSingleton(static services => Loggers.Wrap<IOutboxDispatcher>(
            services, OutboxDispatcher.Create(services),
            static (services, inner) => new OutboxDispatcherLoggingWrapper(
                inner, Loggers.For<OutboxDispatcherLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        return new MsgHatchBuilder(services, setup)
            .AddDefaultTenantService<IRegistryQueries>(
                ServiceLifetime.Scoped, static (tenantServices, _) => RegistryManager.Create(tenantServices), RegistryQueriesLoggingWrapper.Wrap)
            .AddDefaultTenantService<IRegistryCommands>(
                ServiceLifetime.Scoped, static (tenantServices, _) => RegistryManager.Create(tenantServices), RegistryCommandsLoggingWrapper.Wrap)
            .AddDefaultTenantService<IDeliveryQueries>(
                ServiceLifetime.Scoped, static (tenantServices, key) => DeliveryManager.Create(tenantServices, key), DeliveryQueriesLoggingWrapper.Wrap)
            .AddDefaultTenantService<IDeliveryCommands>(
                ServiceLifetime.Scoped, static (tenantServices, key) => DeliveryManager.Create(tenantServices, key), DeliveryCommandsLoggingWrapper.Wrap);
    }

    private static void AddCoreOptions(IServiceCollection services, MsgHatchSetup setup)
    {
        services.AddOptions<MsgHatchOptions>().BindConfiguration(setup.ConfigSectionPath).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MsgHatchOptions>, MsgHatchOptionsValidator>());
        services.AddOptions<RetryOptions>(OptionNames.Inbox)
            .BindConfiguration($"{setup.SectionPathOf(RetryOptions.SectionKey)}:{OptionNames.Inbox}")
            .Validate(static options => options.Expiry is null, _inboxHasNoExpiry)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RetryOptions>, RetryOptionsValidator>());
        AddDispatchOptions(services, setup, OptionNames.Inbox);
        AddDispatchOptions(services, setup, OptionNames.Outbox);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DispatchOptions>, DispatchOptionsValidator>());
        services.AddOptions<MsgHatchSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MsgHatchSetupCheck>, MsgHatchSetupValidator>());
    }

    private static void AddDispatchOptions(IServiceCollection services, MsgHatchSetup setup, string name) =>
        services.AddOptions<DispatchOptions>(name)
            .BindConfiguration($"{setup.SectionPathOf(DispatchOptions.SectionKey)}:{name}")
            .ValidateOnStart();

    private static MsgHatchSetup? ExistingSetup(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(MsgHatchSetup))?.ImplementationInstance
            as MsgHatchSetup;

    private static MsgHatchSetup AddSetup(IServiceCollection services, string configSectionPath)
    {
        var setup = new MsgHatchSetup(configSectionPath);
        services.AddSingleton(setup);
        AddCoreOptions(services, setup);
        return setup;
    }
}
