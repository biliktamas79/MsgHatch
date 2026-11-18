using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AdminUI;

/// <summary>
/// The base class of an editor of a template text (D240). The edit page of a part shows the editor of the engine that reads
/// the part. A package registers an editor for an engine name with
/// <see cref="DependencyInjection.TemplatesAdminUIExtensions.AddTemplateEditor{TEditor}"/>. Each other part gets the plain
/// editor, a text area.
/// </summary>
public abstract class TemplateEditorBase : ComponentBase
{
    /// <summary>The message type, the language, the template scope, the part, and the kind of the text.</summary>
    [Parameter]
    public TemplateLookup Lookup { get; set; }

    /// <summary>The text of the draft.</summary>
    [Parameter]
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets each change of the text.</summary>
    [Parameter]
    public EventCallback<string> TextChanged { get; set; }

    /// <summary>The issues that the page found, for example the message of <see cref="Errors.TemplateInvalid"/> from a save.</summary>
    [Parameter]
    public IReadOnlyList<TemplateIssue> Issues { get; set; } = [];

    /// <summary>True while the page saves or deletes, so the editor accepts no change.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    /// <summary>
    /// Runs before a save. An editor that checks the text, for example the Liquid editor (D242), checks it here, and gives false
    /// to stop the save when the text has an issue.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when the page can save the text.</returns>
    public virtual ValueTask<bool> CanSaveAsync(CancellationToken ct) => ValueTask.FromResult(true);

    /// <summary>Gives the text of an issue, with its line and column when it has them.</summary>
    /// <param name="issue">The issue.</param>
    /// <returns>For example <c>Line 3, column 9: The tag has no end.</c>.</returns>
    protected static string TextOf(TemplateIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return issue is { Line: { } line, Column: { } column } ? $"Line {line}, column {column}: {issue.Message}" : issue.Message;
    }

    /// <summary>Checks a draft of the text with the template queries of the host, and saves nothing (D241).</summary>
    /// <param name="text">The text of the draft.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The issues of the text, an empty list for a text that passes, or the error of the check.</returns>
    protected ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckAsync(string text, CancellationToken ct) =>
        Services.GetRequiredKeyedService<ITemplateQueries>(TenantKey.None).CheckTemplateAsync(Lookup, text, ct);
}
