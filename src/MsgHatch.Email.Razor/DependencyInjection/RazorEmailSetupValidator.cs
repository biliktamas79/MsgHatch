using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Razor.DependencyInjection;

/// <summary>
/// The startup validation of the Razor setup (section 4.7): one call of <c>UseRazorEmailTemplates</c> (D62), and with
/// the role <see cref="MsgHatchRoles.Consumer"/>, the Razor templates and a template store for each Razor registration.
/// </summary>
internal sealed class RazorEmailSetupValidator(IServiceProvider services, RazorEmailSetup setup) : IValidateOptions<RazorEmailSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, RazorEmailSetupCheck options)
    {
        var consumer = services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.Roles.HasFlag(MsgHatchRoles.Consumer);
        string[] failures =
        [
            .. setup.TemplateSelections.Count > 1 ? [$"{string.Join(" and ", setup.TemplateSelections)} select the Razor email templates."] : Array.Empty<string>(),
            .. consumer && setup.TemplateSelections.Count == 0 ? RegistrationsWith("no setup method calls UseRazorEmailTemplates") : [],
            .. consumer && !HasTemplateStore() ? RegistrationsWith("no setup method selects a template store for its subject, for example UseEmbeddedTemplates") : [],
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private IEnumerable<string> RegistrationsWith(string problem) =>
        setup.Registrations.Select(contentType => $"The email registration {contentType} uses the Razor engine, but {problem}.");

    private bool HasTemplateStore() =>
        services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ITemplateStore))
        || services.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(typeof(ITemplateStore), TenantKey.None);
}
