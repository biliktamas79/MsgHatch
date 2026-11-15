using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Liquid;

/// <summary>
/// The Liquid engine (D203, D205, section 4.10). It selects a template with the fallback of D123 and the company
/// folders of D199, and parses its parts one time for each <see cref="TemplateKey"/> (D210).
/// </summary>
/// <typeparam name="TPayload">The payload type, the model of the template (D209).</typeparam>
/// <param name="selector">The template selector of the tenant key.</param>
/// <param name="time">The clock of the value <c>now</c>.</param>
internal sealed class LiquidEmailEngine<TPayload>(TemplateSelector selector, TimeProvider time) : IEmailTemplateEngine<TPayload>
{
    public async ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var source = await selector.FindAsync(request, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        var parts = LiquidTemplateCache.GetOrParse(source.Value);
        return parts.IsError
            ? parts.Error
            : ErrorOr<IEmailTemplate<TPayload>>.ForValue(new LiquidEmailTemplate<TPayload>(parts.Value, time));
    }
}
