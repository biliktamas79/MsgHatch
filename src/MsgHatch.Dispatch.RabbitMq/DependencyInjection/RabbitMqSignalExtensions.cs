using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Dispatch.RabbitMq.DependencyInjection;

/// <summary>The setup method of the RabbitMQ dispatch signal (section 4.7, D305).</summary>
public static class RabbitMqSignalExtensions
{
    /// <summary>
    /// Selects the dispatch signal between the processes of one system on different machines, through a fanout exchange of
    /// RabbitMQ (D305). Each process of the system calls it with the same broker and the same
    /// <see cref="RabbitMqSignalOptions.ExchangeName"/>. The options bind from the section <c>MsgHatch:RabbitMqSignal</c>,
    /// and a value from code wins. The startup validation fails when <see cref="DispatchOptions.EnablePolling"/> is false
    /// for the inbox or the outbox (D190).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRabbitMqDispatchSignal(this MsgHatchBuilder builder, Action<RabbitMqSignalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<RabbitMqSignalOptions>().BindConfiguration(RabbitMqSignalOptions.SectionName).ValidateOnStart();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RabbitMqSignalOptions>, RabbitMqSignalOptionsValidator>());
        RequirePolling(builder.Services, "Inbox");
        RequirePolling(builder.Services, "Outbox");
        return builder.UseDispatchSignal(
            static services => new RabbitMqDispatchSignal(
                services.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value,
                services.GetRequiredService<TimeProvider>(),
                services.GetService<ILogger<RabbitMqDispatchSignal>>() ?? NullLogger<RabbitMqDispatchSignal>.Instance),
            nameof(UseRabbitMqDispatchSignal));
    }

    private static void RequirePolling(IServiceCollection services, string dispatcher) =>
        services.AddOptions<DispatchOptions>(dispatcher)
            .Validate(
                static options => options.EnablePolling,
                $"UseRabbitMqDispatchSignal needs EnablePolling true in the dispatch options {dispatcher}, because a lost signal then waits only until the next poll (D190).")
            .ValidateOnStart();
}
