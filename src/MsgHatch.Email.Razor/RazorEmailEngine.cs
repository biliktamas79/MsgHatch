using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Placeholders;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Razor;

/// <summary>
/// The Razor engine (section 4.10, D193 to D202), a tenant service. It tries the language of the request, and then the
/// default language (D194). In a language, it needs the component of the company of the request or with no company
/// (D195), and the subject of the template store with the lookups of D123 in that language (D197).
/// </summary>
/// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
internal sealed class RazorEmailEngine<TPayload>(
    RazorTemplateClasses classes, ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage,
    IServiceProvider services, ILoggerFactory loggerFactory)
    : IEmailTemplateEngine<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    public async ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var template = await PrepareInLanguageAsync(request, request.Language, ct);
        return IsNotFound(template) && !string.Equals(request.Language, defaultLanguage, StringComparison.OrdinalIgnoreCase)
            ? await PrepareInLanguageAsync(request, defaultLanguage, ct)
            : template;
    }

    private async ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareInLanguageAsync(
        EmailTemplateRequest request, string language, CancellationToken ct)
    {
        var component = classes.Find(request.Type, language, request.Scope) ?? classes.Find(request.Type, language, scope: null);
        if (component is null)
        {
            return new Error(Errors.NotFound, $"No Razor template of {request.Type.Name} exists in {classes.RootNamespace} in the language {language}.");
        }

        if (!typeof(RazorEmailComponent<TPayload>).IsAssignableFrom(component))
        {
            return new Error(Errors.TemplateInvalid, $"The Razor template {component.FullName} has another payload type than {typeof(TPayload).Name}.");
        }

        var subjectAndText = await new PlaceholderEmailEngine<TPayload>(tenantStore, sharedStore, language, PlaceholderParts.SubjectAndText)
            .PrepareAsync(request with { Language = language }, ct);
        return subjectAndText.IsError
            ? subjectAndText.Error
            : new RazorEmailTemplate<TPayload>(subjectAndText.Value, component, services, loggerFactory);
    }

    private static bool IsNotFound(ErrorOr<IEmailTemplate<TPayload>> template) => template.IsError && template.Error.Type == Errors.NotFound;
}
