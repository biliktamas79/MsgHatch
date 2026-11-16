using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

/// <summary>
/// Finds and prepares the templates of one payload type (D44, D213, section 4.10). Another implementation can replace
/// an engine. An implementation caches the parsed objects that it needs for a render. An engine is a tenant service
/// (D126). A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
public interface ITemplateEngine<TPayload>
{
    /// <summary>Finds and prepares the template of one part for a message type, language, and template scope.</summary>
    /// <param name="request">The message type, the language, the template scope, and the part.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The prepared template, or <see cref="Errors.NotFound"/> when no template exists.</returns>
    ValueTask<ErrorOr<ITemplate<TPayload>>> PrepareAsync(TemplateRequest request, CancellationToken ct);
}
