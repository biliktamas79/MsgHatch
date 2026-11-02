namespace MsgHatch.Email;

/// <summary>The parts of one email that a template engine renders.</summary>
/// <param name="Subject">The subject.</param>
/// <param name="HtmlBody">The html body, or null.</param>
/// <param name="TextBody">The text body, or null.</param>
public sealed record RenderedEmail(string Subject, string? HtmlBody, string? TextBody);
