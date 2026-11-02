using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Email.DependencyInjection;

/// <summary>
/// The startup validation of the email setup (section 4.7): one selection of the shared template store and one of the
/// template store of each tenant (D62), and with the role <see cref="MsgHatchRoles.Consumer"/>, a template store for
/// each email registration with the placeholder engine.
/// </summary>
internal sealed class EmailSetupValidator(IServiceProvider services, EmailSetup setup) : IValidateOptions<EmailSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, EmailSetupCheck options)
    {
        var roles = services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.Roles;
        string[] failures =
        [
            .. DuplicateSelections(setup.SharedStoreSelections, "the shared ITemplateStore"),
            .. DuplicateSelections(setup.TenantStoreSelections, "the ITemplateStore of each tenant"),
            .. roles.HasFlag(MsgHatchRoles.Consumer) ? RegistrationsWithNoTemplateStore() : [],
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> DuplicateSelections(List<string> selections, string service) =>
        selections.Count > 1 ? [$"{string.Join(" and ", selections)} select an implementation of {service}."] : [];

    private IEnumerable<string> RegistrationsWithNoTemplateStore() =>
        setup.SharedStoreSelections.Count + setup.TenantStoreSelections.Count > 0
            ? []
            : setup.PlaceholderRegistrations.Select(static contentType =>
                $"The email registration {contentType} uses the placeholder engine, but no setup method selects a template store, for example UseEmbeddedTemplates.");
}
