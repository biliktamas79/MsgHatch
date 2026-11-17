using System.Globalization;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

/// <summary>
/// One prepared template: the text of one part (D213, section 4.10). A caller must not await one returned
/// <see cref="ValueTask{TResult}"/> two times.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
public interface ITemplate<TPayload>
{
    /// <summary>
    /// The lookup that found the text of this template after the fallback of D123: its language and its template scope.
    /// So the caller can ask for an optional part in the same language and scope (D214).
    /// </summary>
    TemplateLookup Lookup { get; }

    /// <summary>Renders the template for one payload.</summary>
    /// <param name="payload">The payload of the route.</param>
    /// <param name="culture">The culture of the language of the route, for the formats.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The output string, or the error.</returns>
    ValueTask<ErrorOr<string>> RenderAsync(TPayload payload, CultureInfo culture, CancellationToken ct);
}
