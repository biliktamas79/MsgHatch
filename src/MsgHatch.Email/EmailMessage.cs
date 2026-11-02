namespace MsgHatch.Email;

/// <summary>
/// The channel envelope of an email: the rendered email, with no copy of the payload (D17). It has at least one body.
/// </summary>
/// <param name="To">The address of the recipient.</param>
/// <param name="Subject">The subject.</param>
/// <param name="HtmlBody">The html body, or null.</param>
/// <param name="TextBody">The text body, or null.</param>
/// <param name="From">The sender address, or null for the sender of the email client.</param>
/// <param name="Cc">The copy addresses, or null.</param>
/// <param name="Bcc">The blind copy addresses, or null.</param>
public sealed record EmailMessage(
    string To, string Subject, string? HtmlBody, string? TextBody = null,
    string? From = null, IReadOnlyList<string>? Cc = null, IReadOnlyList<string>? Bcc = null);
