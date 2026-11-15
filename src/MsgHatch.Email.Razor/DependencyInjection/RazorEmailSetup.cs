using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MsgHatch.Email.Razor.DependencyInjection;

/// <summary>
/// What the Razor setup methods registered: one instance in the service collection, for the startup validation of
/// <see cref="RazorEmailSetupValidator"/>.
/// </summary>
internal sealed class RazorEmailSetup
{
    public List<string> TemplateSelections { get; } = [];

    public List<string> Registrations { get; } = [];

    public static RazorEmailSetup Of(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(RazorEmailSetup))?.ImplementationInstance as RazorEmailSetup
        ?? Add(services);

    private static RazorEmailSetup Add(IServiceCollection services)
    {
        var setup = new RazorEmailSetup();
        services.AddSingleton(setup);
        services.AddOptions<RazorEmailSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RazorEmailSetupCheck>, RazorEmailSetupValidator>());
        return setup;
    }
}

/// <summary>The options type of the startup validation of the Razor setup. It holds no setting.</summary>
internal sealed class RazorEmailSetupCheck;
