using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MsgHatch.Subscriptions.DependencyInjection;

/// <summary>
/// What the setup methods of the subscriptions package registered: one instance in the service collection, for the
/// startup validation of the package (section 4.7, D62).
/// </summary>
internal sealed class SubscriptionSetup
{
    public List<string> StoreSelections { get; } = [];

    public List<string> DirectorySelections { get; } = [];

    public bool UsesSubscriptions { get; set; }

    public static SubscriptionSetup Of(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(SubscriptionSetup))?.ImplementationInstance as SubscriptionSetup
        ?? Add(services);

    private static SubscriptionSetup Add(IServiceCollection services)
    {
        var setup = new SubscriptionSetup();
        services.AddSingleton(setup);
        services.AddOptions<SubscriptionSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SubscriptionSetupCheck>, SubscriptionSetupValidator>());
        return setup;
    }
}
