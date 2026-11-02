using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Templates;

/// <summary>
/// Gives the templates of the placeholder engine (section 4.10). The unkeyed store is the shared template store. The
/// template store of a tenant is a keyed service with a <c>TenantKey</c> key (D123). A caller must not await one
/// returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface ITemplateStore
{
    /// <summary>Finds the template of exactly this message type, language, and template scope.</summary>
    /// <param name="lookup">The message type, the language, and the template scope.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The template, null when no template matches, or the error of the storage.</returns>
    ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct);
}
