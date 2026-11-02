using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Email.Resend;

/// <summary>The settings of the Resend email client.</summary>
public sealed class ResendOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:Resend";

    /// <summary>The API key of Resend, for example <c>re_123</c>. A key with the permission "sending access" is enough.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The sender address of each email with no address of its own, for example <c>noreply@example.com</c>. Its domain must
    /// be verified in Resend. The test sender <c>onboarding@resend.dev</c> needs no verified domain, and reaches only the
    /// test addresses of Resend and the address of the Resend account.
    /// </summary>
    [Required]
    [EmailAddress]
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>The name of the sender, or null for no name.</summary>
    [MaxLength(100)]
    public string? SenderName { get; set; }

    /// <summary>
    /// The sender address: <c>"{SenderName} &lt;{SenderEmail}&gt;"</c>, or only <see cref="SenderEmail"/> when the name is
    /// empty, or when <see cref="SenderEmail"/> already contains <c>&lt;</c> and <c>&gt;</c>.
    /// </summary>
    internal string SenderAddress =>
        string.IsNullOrWhiteSpace(SenderName) || (SenderEmail.Contains('<', StringComparison.Ordinal) && SenderEmail.Contains('>', StringComparison.Ordinal))
            ? SenderEmail
            : $"{SenderName} <{SenderEmail}>";
}
