using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The entry point of the MsgHatch setup.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core services of MsgHatch: the options, the in-memory <see cref="IMessageRegistry"/> from the
    /// registrations (D117), the content type registry, <see cref="MsgHatchJson"/>, and <see cref="TimeProvider.System"/>
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
        services.AddOptions<MsgHatchSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MsgHatchSetupCheck>, MsgHatchSetupValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageRegistry>(_ =>
            new InMemoryMessageRegistry(setup.Channels, setup.Messages.Select(static message => message.Descriptor)));
        services.TryAddSingleton<IContentTypeRegistry>(_ => new ContentTypeRegistry(setup.ContentTypes));
        services.TryAddSingleton<MsgHatchJson>();
        services.TryAddScoped<NotificationPipeline>();
        services.TryAddScoped(static services => Loggers.Wrap<INotificationService>(
            services, new NotificationService(services.GetRequiredService<NotificationPipeline>()),
            static (services, inner) => new NotificationServiceLoggingWrapper(
                inner, Loggers.For<NotificationServiceLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())));
        return new MsgHatchBuilder(services, setup);
    }

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
