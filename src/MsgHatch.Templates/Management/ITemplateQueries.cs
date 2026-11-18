using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The reads of the template administration (D222, D231, section 4.22). It is a tenant service (D126): the queries of
/// <c>TenantKey.None</c> use the shared template store, and the queries of a tenant use only the template store of that
/// tenant. They validate each request. A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface ITemplateQueries
{
    /// <summary>Lists the templates of the store that match all given filters.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>One entry for each template, <see cref="Errors.ValidationFailed"/>, <see cref="Errors.NotFound"/> for no store, or the error of the store.</returns>
    ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> GetTemplatesAsync(TemplateQuery query, CancellationToken ct);

    /// <summary>Reads the text of exactly one template of the store, with no fallback.</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The text with its key, <see cref="Errors.ValidationFailed"/>, <see cref="Errors.NotFound"/>, or the error of the store.</returns>
    ValueTask<ErrorOr<TemplateSource>> GetTemplateAsync(TemplateLookup lookup, CancellationToken ct);

    /// <summary>
    /// Gives each declared part of each message type with a template engine, or only of <paramref name="type"/>, with the names
    /// of the engines of the registrations that read it (D220, D240). A message type with no registration gives an empty list.
    /// </summary>
    /// <param name="type">The message type, or null for each message type.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>One entry for each declared part and kind, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<TemplatePartInfo>>> GetTemplatePartsAsync(MessageType? type, CancellationToken ct);

    /// <summary>
    /// Checks a draft of one template with the engine of each registration that reads the part, and saves nothing (D241).
    /// </summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="text">The text of the draft.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The issues of the text, an empty list for a text that passes, <see cref="Errors.ValidationFailed"/>,
    /// <see cref="Errors.NotFound"/> for a message type with no registration, or the error of an engine.
    /// </returns>
    ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct);

    /// <summary>
    /// Renders a draft of one template with the engine and the sample payload of each registration that reads the part and has
    /// a sample, and saves nothing (D254, D256).
    /// </summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="text">The text of the draft.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// One preview for each registration with a sample, an empty list for a part with no sample, <see cref="Errors.ValidationFailed"/>,
    /// <see cref="Errors.NotFound"/> for a message type with no registration, <see cref="Errors.TemplateInvalid"/> for a text
    /// that does not parse or render, or the error of an engine.
    /// </returns>
    ValueTask<ErrorOr<IReadOnlyList<TemplatePreview>>> PreviewTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct);
}
