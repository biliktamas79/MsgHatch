using System.Globalization;
using System.Text.RegularExpressions;
using Fluid;
using Fluid.Ast;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Liquid;

/// <summary>
/// The check of a Liquid text (D219, D241): a parse with no cache entry, and a walk of the parsed tree that finds a filter
/// that the filter collection does not have (D211), and a <c>render</c> or <c>include</c> tag (D208). A check has no
/// payload, so an unknown name stays an error of the render (D207). A parse error has the line and the column of Fluid
/// (D243). The check gives one issue for the first problem that it finds.
/// </summary>
/// <param name="filters">The filters of the options of the payload type.</param>
internal sealed partial class LiquidTemplateCheck(FilterCollection filters) : AstVisitor
{
    private string? _fault;

    public static IReadOnlyList<TemplateIssue> Check(TemplateLookup lookup, string text, FilterCollection filters)
    {
        var draft = DraftOf(lookup, text);
        var template = LiquidTemplateCache.Parse(draft);
        if (template.IsError)
        {
            return [ParseIssueOf(template.Error.Message ?? template.Error.Type.Name)];
        }

        var check = new LiquidTemplateCheck(filters);
        check.VisitTemplate(template.Value);
        return check._fault is { } fault ? [new TemplateIssue($"The template {draft.Key.Id} {fault}.", Line: null, Column: null)] : [];
    }

    /// <summary>Gives a text that is not stored the key of its lookup, so that a check and a render of a draft name it in one way (D255).</summary>
    public static TemplateSource DraftOf(TemplateLookup lookup, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TemplateSource(new TemplateKey($"{lookup.Type.Name}.{lookup.Language}.{lookup.Part}", 0), text);
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

    private static TemplateIssue ParseIssueOf(string message)
    {
        var position = FluidPosition().Match(message);
        return position.Success
            ? new TemplateIssue(message, NumberOf(position.Groups["line"]), NumberOf(position.Groups["column"]))
            : new TemplateIssue(message, Line: null, Column: null);
    }

    private static int NumberOf(Group group) => int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);

    // Fluid 2.41.1 gives a parse error only as text, with " at (line:column)" at the end of its first line: the grammar
    // gives a ParseError with a position, but an unknown tag throws Fluid.ParseException with no position.
    [GeneratedRegex(@" at \((?<line>[0-9]{1,9}):(?<column>[0-9]{1,9})\)(?:\r?\n|$)")]
    private static partial Regex FluidPosition();
}
