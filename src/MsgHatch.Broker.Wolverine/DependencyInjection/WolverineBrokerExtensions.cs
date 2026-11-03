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
    /// RabbitMQ or Amazon SQS. The startup validation fails when the host did not register Wolverine, or when
    /// <c>Durability.UseSyncRetryBlock</c> of Wolverine is false.
    /// </summary>
    /// <remarks>
    /// A broker message is sent only when Wolverine throws for each send that the broker does not accept. So the host
    /// configures Wolverine in this way (section 4.7):
    /// <list type="bullet">
    /// <item><c>SendInline()</c> on the publish rule of each broker payload type. A buffered endpoint returns before the
    /// send.</item>
    /// <item><c>Durability.UseSyncRetryBlock = true</c>. With the default retry block, an inline send that fails returns
    /// with no exception, and Wolverine retries it in the background and then drops it.</item>
    /// <item>The publisher confirms of the transport, for example <c>PublisherConfirmationsEnabled</c> and
    /// <c>PublisherConfirmationTrackingEnabled</c> of RabbitMQ.</item>
    /// </list>
    /// </remarks>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseWolverineBroker(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<WolverineBrokerSetupCheck>()
            .Validate<IServiceProviderIsService>(
                static (_, services) => services.IsService(typeof(IWolverineRuntime)),
                "UseWolverineBroker needs the Wolverine of the host. Register it with UseWolverine or AddWolverine.")
            .Validate<IServiceProvider>(
                static (_, services) => services.GetService<IWolverineRuntime>() is null or { Options.Durability.UseSyncRetryBlock: true },
                "UseWolverineBroker needs Durability.UseSyncRetryBlock = true in Wolverine, so that a send that fails throws.")
            .ValidateOnStart();
        return builder.UseBrokerClient(static services =>
            new WolverineBrokerClient(() => new MessageBus(services.GetRequiredService<IWolverineRuntime>())));
    }
}
