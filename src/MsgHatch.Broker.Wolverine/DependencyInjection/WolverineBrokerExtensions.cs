using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using Wolverine.Runtime;

namespace MsgHatch.Broker.Wolverine.DependencyInjection;

/// <summary>The setup method of the Wolverine broker package (section 4.7, D60, D134).</summary>
public static class WolverineBrokerExtensions
{
    /// <summary>
    /// Selects <see cref="WolverineBrokerClient"/> as the broker client, with its logging wrapper, and the broker
    /// transport. It uses the Wolverine of the host, because the host selects the transport of Wolverine, for example
    /// RabbitMQ or Amazon SQS. The startup validation fails when the host did not register Wolverine.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseWolverineBroker(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<WolverineBrokerSetupCheck>()
            .Validate<IServiceProviderIsService>(
                static (_, services) => services.IsService(typeof(IWolverineRuntime)),
                "UseWolverineBroker needs the Wolverine of the host. Register it with UseWolverine or AddWolverine.")
            .ValidateOnStart();
        return builder.UseBrokerClient(static services =>
            new WolverineBrokerClient(() => new MessageBus(services.GetRequiredService<IWolverineRuntime>())));
    }
}
