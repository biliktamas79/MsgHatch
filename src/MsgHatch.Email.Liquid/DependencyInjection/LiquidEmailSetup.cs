using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Liquid.DependencyInjection;

/// <summary>
/// What <c>AsLiquidEmail</c> registered: one instance in the service collection, for the startup validation of
/// <see cref="LiquidEmailSetupValidator"/>.
/// </summary>
internal sealed class LiquidEmailSetup
{
    public List<string> Registrations { get; } = [];

    public static LiquidEmailSetup Of(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(LiquidEmailSetup))?.ImplementationInstance as LiquidEmailSetup
        ?? Add(services);

    private static LiquidEmailSetup Add(IServiceCollection services)
    {
        var setup = new LiquidEmailSetup();
        services.AddSingleton(setup);
        services.AddOptions<LiquidEmailSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<LiquidEmailSetupCheck>, LiquidEmailSetupValidator>());
        return setup;
    }
}

/// <summary>The options type of the startup validation of the Liquid setup. It holds no setting.</summary>
internal sealed class LiquidEmailSetupCheck;

/// <summary>
/// The startup validation of the Liquid setup (section 4.7): with the role <see cref="MsgHatchRoles.Consumer"/>, a
/// template store for each Liquid registration, as for a registration of the placeholder engine.
/// </summary>
internal sealed class LiquidEmailSetupValidator(IServiceProvider services, LiquidEmailSetup setup) : IValidateOptions<LiquidEmailSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, LiquidEmailSetupCheck options)
    {
        var consumer = services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.Roles.HasFlag(MsgHatchRoles.Consumer);
        return consumer && !HasTemplateStore()
            ? ValidateOptionsResult.Fail(setup.Registrations.Select(static contentType =>
                $"The email registration {contentType} uses the Liquid engine, but no setup method selects a template store, for example UseEmbeddedTemplates."))
            : ValidateOptionsResult.Success;
    }

    private bool HasTemplateStore() =>
        services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ITemplateStore))
        || services.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(typeof(ITemplateStore), TenantKey.None);
}
