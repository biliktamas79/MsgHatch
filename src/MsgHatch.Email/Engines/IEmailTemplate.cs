using System.Globalization;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Engines;

/// <summary>
/// One prepared email template (section 4.10). A caller must not await one returned <see cref="ValueTask{TResult}"/>
/// two times.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
public interface IEmailTemplate<TPayload>
{
    /// <summary>Renders the template for one payload.</summary>
    /// <param name="payload">The payload of the route.</param>
    /// <param name="route">The route, so that an engine can read its trace.</param>
    /// <param name="culture">The culture of the language of the route, for the formats.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The rendered parts, or the error.</returns>
    ValueTask<ErrorOr<RenderedEmail>> RenderAsync(TPayload payload, MessageRoute route, CultureInfo culture, CancellationToken ct);
}
