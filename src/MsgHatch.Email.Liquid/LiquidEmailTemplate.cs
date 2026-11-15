using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Fluid;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Liquid;

/// <summary>
/// One prepared template of the Liquid engine (D210). A render has the payload as its model (D209), the culture of the
/// route, the time zone UTC, and the <c>now</c> of the <see cref="TimeProvider"/>. The html part encodes each value,
/// and the subject and the text encode nothing. Each part renders in its own scope, so a variable of one part is
/// unknown in the next part.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
/// <param name="parts">The parsed parts.</param>
/// <param name="time">The clock of the value <c>now</c>.</param>
internal sealed class LiquidEmailTemplate<TPayload>(LiquidParts parts, TimeProvider time) : IEmailTemplate<TPayload>
{
    private static readonly HtmlEncoder _html = HtmlEncoder.Create(UnicodeRanges.All);

    public async ValueTask<ErrorOr<RenderedEmail>> RenderAsync(TPayload payload, MessageRoute route, CultureInfo culture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var context = new TemplateContext(payload, LiquidOptions<TPayload>.Options) { CultureInfo = culture, Now = time.GetUtcNow };
        try
        {
            return new RenderedEmail(
                await parts.Subject.RenderAsync(context, NullEncoder.Default),
                await parts.Html.RenderAsync(context, _html),
                parts.Text is null ? null : await parts.Text.RenderAsync(context, NullEncoder.Default));
        }
        catch (Exception exception) when (IsTemplateError(exception))
        {
            return new Error(Errors.TemplateInvalid, exception.Message);
        }
    }

    /// <summary>
    /// An unknown name (D207), the step limit (D210), and a partial (D208) are template errors. An exception that the
    /// payload code throws is not, so it reaches the caller.
    /// </summary>
    private static bool IsTemplateError(Exception exception) =>
        exception is FluidException || exception.TargetSite?.DeclaringType?.Assembly == typeof(FluidParser).Assembly;
}
