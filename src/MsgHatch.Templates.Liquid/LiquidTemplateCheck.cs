using Fluid;
using Fluid.Ast;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Liquid;

/// <summary>
/// The check of a Liquid text before a write (D219): a parse with no cache entry, and a walk of the parsed tree that
/// finds a filter that the filter collection does not have (D211), and a <c>render</c> or <c>include</c> tag (D208).
/// A check has no payload, so an unknown name stays an error of the render (D207).
/// </summary>
/// <param name="filters">The filters of the options of the payload type.</param>
internal sealed class LiquidTemplateCheck(FilterCollection filters) : AstVisitor
{
    private string? _fault;

    public static Error? Check(TemplateLookup lookup, string text, FilterCollection filters)
    {
        ArgumentNullException.ThrowIfNull(text);
        var id = $"{lookup.Type.Name}.{lookup.Language}.{lookup.Part}";
        var template = LiquidTemplateCache.Parse(new TemplateSource(new TemplateKey(id, 0), text));
        if (template.IsError)
        {
            return template.Error;
        }

        var check = new LiquidTemplateCheck(filters);
        check.VisitTemplate(template.Value);
        return check._fault is { } fault ? new Error(Errors.TemplateInvalid, $"The template {id} {fault}.") : null;
    }

    protected override Expression VisitFilterExpression(FilterExpression filterExpression)
    {
        if (!filters.TryGetValue(filterExpression.Name, out _))
        {
            _fault ??= $"has the unknown filter {filterExpression.Name}";
        }

        return base.VisitFilterExpression(filterExpression);
    }

    protected override Statement VisitRenderStatement(RenderStatement renderStatement)
    {
        _fault ??= "has a render tag, and the engine reads no partial";
        return base.VisitRenderStatement(renderStatement);
    }

    protected override Statement VisitIncludeStatement(IncludeStatement includeStatement)
    {
        _fault ??= "has an include tag, and the engine reads no partial";
        return base.VisitIncludeStatement(includeStatement);
    }
}
