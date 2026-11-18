using MsgHatch.Domain.Failures;

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
}
