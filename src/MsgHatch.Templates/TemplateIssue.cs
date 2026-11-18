namespace MsgHatch.Templates;

/// <summary>
/// One problem of a template text that a check finds (D241). The line and the column start at 1, as the engine gives
/// them, and are null when the engine gives no position, for example for an unknown filter.
/// </summary>
/// <param name="Message">The message of the problem.</param>
/// <param name="Line">The line of the problem, or null.</param>
/// <param name="Column">The column of the problem, or null.</param>
public sealed record TemplateIssue(string Message, int? Line, int? Column);
