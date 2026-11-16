using MsgHatch.Domain.Failures;
using MsgHatch.Templates;

namespace MsgHatch.Email.Liquid;

/// <summary>
/// The Liquid engine (D203, D216, section 4.10). It selects the template of one part with the fallback of D123 and the
/// company folders of D199, and parses it one time for each <see cref="TemplateKey"/> (D210).
/// </summary>
/// <typeparam name="TPayload">The payload type, the model of the template (D209).</typeparam>
/// <param name="selector">The template selector of the tenant key.</param>
/// <param name="time">The clock of the value <c>now</c>.</param>
internal sealed class LiquidTemplateEngine<TPayload>(TemplateSelector selector, TimeProvider time) : ITemplateEngine<TPayload>
{
    public async ValueTask<ErrorOr<ITemplate<TPayload>>> PrepareAsync(TemplateRequest request, CancellationToken ct)
    {
        var source = await selector.FindAsync(request, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        var template = LiquidTemplateCache.GetOrParse(source.Value);
        return template.IsError
            ? template.Error
            : ErrorOr<ITemplate<TPayload>>.ForValue(new LiquidTemplate<TPayload>(template.Value, request.Kind, time));
    }
}
