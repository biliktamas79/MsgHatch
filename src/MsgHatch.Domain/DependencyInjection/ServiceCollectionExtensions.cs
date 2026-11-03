using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Inbox;
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
    /// as the <see cref="IDispatchSignal"/>, and <see cref="TimeProvider.System"/>
    /// if the host has registered no <see cref="TimeProvider"/>. The options bind from the section <c>MsgHatch</c>
    /// of the <c>IConfiguration</c> of the host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional change of the options in code. A value from code wins.</param>
    /// <returns>The builder for the other setup methods.</returns>
    public static MsgHatchBuilder AddMsgHatch(this IServiceCollection services, Action<MsgHatchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var setup = ExistingSetup(services) ?? AddSetup(services);
        var options = services.AddOptions<MsgHatchOptions>().BindConfiguration(MsgHatchOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MsgHatchOptions>, MsgHatchOptionsValidator>());
        services.AddOptions<RetryOptions>(OptionNames.Inbox)
            .BindConfiguration($"{RetryOptions.SectionName}:{OptionNames.Inbox}")
            .Validate(static options => options.Expiry is null, _inboxHasNoExpiry)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RetryOptions>, RetryOptionsValidator>());
        AddDispatchOptions(services, OptionNames.Inbox);
        AddDispatchOptions(services, OptionNames.Outbox);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DispatchOptions>, DispatchOptionsValidator>());
        services.AddOptions<MsgHatchSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MsgHatchSetupCheck>, MsgHatchSetupValidator>());
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
        return new MsgHatchBuilder(services, setup);
    }

    private static void AddDispatchOptions(IServiceCollection services, string name) =>
        services.AddOptions<DispatchOptions>(name).BindConfiguration($"{DispatchOptions.SectionName}:{name}").ValidateOnStart();

    private static MsgHatchSetup? ExistingSetup(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(MsgHatchSetup))?.ImplementationInstance
            as MsgHatchSetup;

    private static MsgHatchSetup AddSetup(IServiceCollection services)
    {
        var setup = new MsgHatchSetup();
        services.AddSingleton(setup);
        return setup;
    }
}
