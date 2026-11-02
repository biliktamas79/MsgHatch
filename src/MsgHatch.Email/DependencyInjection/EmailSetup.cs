using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MsgHatch.Email.DependencyInjection;

/// <summary>
/// What the email setup methods registered: one instance in the service collection, for the startup validation of
/// the template stores (section 4.7, D62).
/// </summary>
internal sealed class EmailSetup
{
    public List<string> SharedStoreSelections { get; } = [];

    public List<string> TenantStoreSelections { get; } = [];

    public List<string> PlaceholderRegistrations { get; } = [];

    public static EmailSetup Of(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(EmailSetup))?.ImplementationInstance as EmailSetup
        ?? Add(services);

    private static EmailSetup Add(IServiceCollection services)
    {
        var setup = new EmailSetup();
        services.AddSingleton(setup);
        services.AddOptions<EmailSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmailSetupCheck>, EmailSetupValidator>());
        return setup;
    }
}
