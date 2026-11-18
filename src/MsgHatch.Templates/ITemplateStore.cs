using System.Globalization;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

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

    /// <summary>
    /// Gives the text of a version of this store for a user, for example a date. Only the store knows the format of its
    /// versions, and each caller treats a version as an opaque number (D267). The default is the number in the invariant
    /// culture.
    /// </summary>
    /// <param name="version">A version of this store.</param>
    /// <returns>The text of the version.</returns>
    string RenderVersion(long version) => version.ToString(CultureInfo.InvariantCulture);
}
