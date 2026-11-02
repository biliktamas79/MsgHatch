using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Email.AmazonSES;

/// <summary>The settings of the Amazon SES email client (section 4.4).</summary>
public sealed class AmazonSesOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:AmazonSes";

    /// <summary>
    /// The sender address of each email with no address of its own, for example <c>noreply@example.com</c>. SES accepts
    /// only 7-bit ASCII in an address, so a domain with other letters must be in Punycode, for example
    /// <c>noreply@xn--plda-bpa.hu</c>.
    /// </summary>
    [Required]
    [EmailAddress]
    [RegularExpression("^[ -~]+$")]
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>The name of the sender, or null for no name.</summary>
    [MaxLength(100)]
    public string? SenderName { get; set; }

    /// <summary>The AWS region of SES, for example <c>eu-west-1</c>, or null for the region of the AWS SDK configuration.</summary>
    [RegularExpression("^[a-z]{2}(-[a-z]+)+-[0-9]$")]
    public string? Region { get; set; }

    /// <summary>
    /// The sender address: <c>"{SenderName} &lt;{SenderEmail}&gt;"</c>, or only <see cref="SenderEmail"/> when the name is
    /// empty, or when <see cref="SenderEmail"/> already contains <c>&lt;</c> and <c>&gt;</c>.
    /// </summary>
    internal string SenderAddress =>
        string.IsNullOrWhiteSpace(SenderName) || (SenderEmail.Contains('<', StringComparison.Ordinal) && SenderEmail.Contains('>', StringComparison.Ordinal))
            ? SenderEmail
            : $"{SenderName} <{SenderEmail}>";
}
