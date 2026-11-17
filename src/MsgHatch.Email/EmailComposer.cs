using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Templates;

namespace MsgHatch.Email;

/// <summary>
/// The composer of an email registration (section 4.8, step 4, D215). It loads the payloads of all routes with one
/// call, prepares the parts <c>subject</c>, <c>body</c> (html), and <c>body</c> (text) one time for each language and
/// template scope, and renders one email for each route, to the address of the route. The text body is optional. A
/// composer is a tenant service (D126), so it gets the loader and the engine of its key.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
/// <param name="loader">The payload loader of the registration.</param>
/// <param name="engine">The template engine of the payload type.</param>
public sealed class EmailComposer<TEvent, TPayload>(IPayloadLoader<TEvent, TPayload> loader, ITemplateEngine<TPayload> engine)
    : IEnvelopeComposer<TEvent, EmailMessage>
{
    private const string _subjectPart = "subject";
    private const string _bodyPart = "body";

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<IReadOnlyList<ErrorOr<EmailMessage>>>> ComposeAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payloads = await loader.LoadAsync(message, routes, ct);
        if (payloads.IsError)
        {
            return payloads.Error;
        }

        if (payloads.Value.Count != routes.Length)
        {
            return new Error(Errors.ValidationFailed, $"The payload loader gave {payloads.Value.Count} payloads for {routes.Length} routes.");
        }

        var emails = new ErrorOr<EmailMessage>[routes.Length];
        foreach (var group in RouteGrouping.ByLanguageAndScope(routes))
        {
            await ComposeGroupAsync(message.Type, group, payloads.Value, emails, ct);
        }

        return ErrorOr<IReadOnlyList<ErrorOr<EmailMessage>>>.ForValue(emails);
    }

    private async ValueTask ComposeGroupAsync(
        MessageType type, RouteGroup<(string Language, string? TemplateScope)> group, IReadOnlyList<TPayload> payloads,
        ErrorOr<EmailMessage>[] emails, CancellationToken ct)
    {
        var template = await PrepareAsync(type, group.Key.Language, group.Key.TemplateScope, ct);
        for (var index = 0; index < group.Routes.Length; index++)
        {
            var position = group.Positions.Span[index];
            var route = group.Routes.Span[index];
            emails[position] = template.IsError
                ? template.Error
                : await RenderAsync(template.Value, route, payloads[position], ct);
        }
    }

    private async ValueTask<ErrorOr<EmailTemplate>> PrepareAsync(MessageType type, string language, string? scope, CancellationToken ct)
    {
        var culture = CultureOf(language);
        if (culture is null)
        {
            return new Error(Errors.ValidationFailed, $"The language {language} is not a known culture.");
        }

        var subject = await engine.PrepareAsync(new TemplateRequest(type, language, scope, _subjectPart, TemplatePartKind.PlainText), ct);
        if (subject.IsError)
        {
            return subject.Error;
        }

        var html = await engine.PrepareAsync(new TemplateRequest(type, language, scope, _bodyPart, TemplatePartKind.Html), ct);
        if (html.IsError)
        {
            return html.Error;
        }

        var text = await engine.PrepareAsync(TextBodyLike(html.Value.Lookup), ct);
        if (text.IsError && text.Error.Type != Errors.NotFound)
        {
            return text.Error;
        }

        return new EmailTemplate(subject.Value, html.Value, text.IsError ? null : text.Value, culture);
    }

    /// <summary>The optional text body in the language and scope where the html body was found, so no email mixes two languages (D214).</summary>
    private static TemplateRequest TextBodyLike(TemplateLookup html) =>
        new(html.Type, html.Language, html.Scope, _bodyPart, TemplatePartKind.PlainText, Optional: true);

    private static async ValueTask<ErrorOr<EmailMessage>> RenderAsync(EmailTemplate template, MessageRoute route, TPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(route.Address))
        {
            return new Error(Errors.ValidationFailed, "The route has no address.");
        }

        if (payload is null)
        {
            return new Error(Errors.ValidationFailed, "The payload loader gave no payload for the route.");
        }

        var subject = await template.Subject.RenderAsync(payload, template.Culture, ct);
        if (subject.IsError)
        {
            return subject.Error;
        }

        var html = route.Settings is EmailChannelSettings { Format: EmailFormat.PlainText }
            ? ErrorOr<string?>.ForValue(null)
            : await RenderPartAsync(template.Html, payload, template.Culture, ct);
        if (html.IsError)
        {
            return html.Error;
        }

        var text = await RenderPartAsync(template.Text, payload, template.Culture, ct);
        if (text.IsError)
        {
            return text.Error;
        }

        return html.Value is null && text.Value is null
            ? new Error(Errors.TemplateInvalid, "The template gives no body for the format of the route.")
            : new EmailMessage(route.Address, subject.Value, html.Value, text.Value);
    }

    private static async ValueTask<ErrorOr<string?>> RenderPartAsync(
        ITemplate<TPayload>? template, TPayload payload, CultureInfo culture, CancellationToken ct)
    {
        if (template is null)
        {
            return ErrorOr<string?>.ForValue(null);
        }

        var rendered = await template.RenderAsync(payload, culture, ct);
        return rendered.IsError ? rendered.Error : ErrorOr<string?>.ForValue(rendered.Value);
    }

    private static CultureInfo? CultureOf(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The prepared parts of the emails of one group of routes, and the culture of their language.</summary>
    private readonly record struct EmailTemplate(
        ITemplate<TPayload> Subject, ITemplate<TPayload> Html, ITemplate<TPayload>? Text, CultureInfo Culture);
}
