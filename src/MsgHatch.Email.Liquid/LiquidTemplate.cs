using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Fluid;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Liquid;

/// <summary>
/// One prepared template of the Liquid engine: one part (D210). A render has the payload as its model (D209), the
/// culture of the route, the time zone UTC, the <c>now</c> of the <see cref="TimeProvider"/>, and its own limit of
/// steps. An <see cref="TemplatePartKind.Html"/> part encodes each value, and a plain text part encodes nothing.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
/// <param name="template">The parsed text.</param>
/// <param name="kind">The kind of the part.</param>
/// <param name="time">The clock of the value <c>now</c>.</param>
internal sealed class LiquidTemplate<TPayload>(IFluidTemplate template, TemplatePartKind kind, TimeProvider time) : ITemplate<TPayload>
{
    private static readonly HtmlEncoder _html = HtmlEncoder.Create(UnicodeRanges.All);

    public async ValueTask<ErrorOr<string>> RenderAsync(TPayload payload, CultureInfo culture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var context = new TemplateContext(payload, LiquidOptions<TPayload>.Options) { CultureInfo = culture, Now = time.GetUtcNow };
        try
        {
            return await template.RenderAsync(context, kind == TemplatePartKind.Html ? _html : NullEncoder.Default);
        }
        catch (Exception exception) when (IsTemplateError(exception))
        {
            return new Error(Errors.TemplateInvalid, exception.Message);
        }
    }

    /// <summary>
    /// An unknown name (D207), an unknown filter (D211), the step limit (D210), and a partial (D208) are template errors.
    /// An exception that the payload code throws is not, so it reaches the caller.
    /// </summary>
    private static bool IsTemplateError(Exception exception) =>
        exception is FluidException || exception.TargetSite?.DeclaringType?.Assembly == typeof(FluidParser).Assembly;
}
