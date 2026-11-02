using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email;

/// <summary>
/// The composer of an email registration (section 4.8, step 4). It loads the payloads of all routes with one call,
/// prepares one template for each language and template scope, and renders one email for each route, to the address
/// of the route. A composer is a tenant service (D126), so it gets the loader and the engine of its key.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
/// <param name="loader">The payload loader of the registration.</param>
/// <param name="engine">The template engine of the payload type.</param>
public sealed class EmailComposer<TEvent, TPayload>(IPayloadLoader<TEvent, TPayload> loader, IEmailTemplateEngine<TPayload> engine)
    : IEnvelopeComposer<TEvent, EmailMessage>
{
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
                : await RenderAsync(template.Value.Template, template.Value.Culture, route, payloads[position], ct);
        }
    }

    private async ValueTask<ErrorOr<(IEmailTemplate<TPayload> Template, CultureInfo Culture)>> PrepareAsync(
        MessageType type, string language, string? scope, CancellationToken ct)
    {
        var culture = CultureOf(language);
        if (culture is null)
        {
            return new Error(Errors.ValidationFailed, $"The language {language} is not a known culture.");
        }

        var template = await engine.PrepareAsync(new EmailTemplateRequest(type, language, scope), ct);
        return template.IsError ? template.Error : (template.Value, culture);
    }

    private static async ValueTask<ErrorOr<EmailMessage>> RenderAsync(
        IEmailTemplate<TPayload> template, CultureInfo culture, MessageRoute route, TPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(route.Address))
        {
            return new Error(Errors.ValidationFailed, "The route has no address.");
        }

        if (payload is null)
        {
            return new Error(Errors.ValidationFailed, "The payload loader gave no payload for the route.");
        }

        var rendered = await template.RenderAsync(payload, route, culture, ct);
        return rendered.IsError ? rendered.Error : EmailOf(route, route.Address, rendered.Value);
    }

    private static ErrorOr<EmailMessage> EmailOf(MessageRoute route, string address, RenderedEmail rendered)
    {
        var htmlBody = route.Settings is EmailChannelSettings { Format: EmailFormat.PlainText } ? null : rendered.HtmlBody;
        return htmlBody is null && rendered.TextBody is null
            ? new Error(Errors.TemplateInvalid, "The template gives no body for the format of the route.")
            : new EmailMessage(address, rendered.Subject, htmlBody, rendered.TextBody);
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
}
