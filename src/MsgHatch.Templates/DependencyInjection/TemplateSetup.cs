using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Templates.DependencyInjection;

/// <summary>
/// What the template setup methods registered: one instance in the service collection, for the startup validation of
/// the template stores and the template engines (section 4.7, D62).
/// </summary>
internal sealed class TemplateSetup
{
    public List<string> SharedStoreSelections { get; } = [];

    public List<string> TenantStoreSelections { get; } = [];

    public List<(Type PayloadType, string Registration, Delegate Engine)> EngineSelections { get; } = [];

    public static TemplateSetup Of(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(TemplateSetup))?.ImplementationInstance as TemplateSetup
        ?? Add(services);

    private static TemplateSetup Add(IServiceCollection services)
    {
        var setup = new TemplateSetup();
        services.AddSingleton(setup);
        services.AddOptions<TemplateSetupCheck>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TemplateSetupCheck>, TemplateSetupValidator>());
        return setup;
    }
}

/// <summary>The options type of the startup validation of the template setup (section 4.7). It holds no setting.</summary>
internal sealed class TemplateSetupCheck;

/// <summary>
/// The startup validation of the template setup (section 4.7): one selection of the shared template store, one of the
/// template store of each tenant, and one engine factory for each payload type (D62), and with the role
/// <see cref="MsgHatchRoles.Consumer"/>, a template store for each registration of a template engine.
/// </summary>
internal sealed class TemplateSetupValidator(IServiceProvider services, TemplateSetup setup) : IValidateOptions<TemplateSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, TemplateSetupCheck options)
    {
        var roles = services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.Roles;
        string[] failures =
        [
            .. DuplicateSelections(setup.SharedStoreSelections, "the shared ITemplateStore"),
            .. DuplicateSelections(setup.TenantStoreSelections, "the ITemplateStore of each tenant"),
            .. TwoEnginesForOnePayloadType(),
            .. roles.HasFlag(MsgHatchRoles.Consumer) ? RegistrationsWithNoTemplateStore() : [],
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> DuplicateSelections(List<string> selections, string service) =>
        selections.Count > 1 ? [$"{string.Join(" and ", selections)} select an implementation of {service}."] : [];

    private IEnumerable<string> TwoEnginesForOnePayloadType() =>
        setup.EngineSelections
            .GroupBy(static selection => selection.PayloadType)
            .Where(static payload => payload.Select(static selection => selection.Engine).Distinct().Skip(1).Any())
            .Select(static payload =>
                $"{string.Join(" and ", payload.Select(static selection => selection.Registration))} select two template engines for the payload type {payload.Key.Name}. Give each registration of one payload type the same engine factory.");

    private IEnumerable<string> RegistrationsWithNoTemplateStore() =>
        setup.SharedStoreSelections.Count + setup.TenantStoreSelections.Count > 0
            ? []
            : setup.EngineSelections.Select(static selection =>
                $"The registration {selection.Registration} uses a template engine, but no setup method selects a template store, for example UseEmbeddedTemplates.");
}
