using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Dispatch.RabbitMq;

/// <summary>The settings of the RabbitMQ dispatch signal (D305).</summary>
public sealed class RabbitMqSignalOptions : IValidatableObject
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "MsgHatch:RabbitMqSignal";

    /// <summary>
    /// The AMQP URI of the broker, for example <c>amqp://user:password@rabbit.example.com:5672/vhost</c>, with the scheme
    /// <c>amqp</c> or <c>amqps</c>. It has no default.
    /// </summary>
    [Required]
    public Uri? Uri { get; set; }

    /// <summary>
    /// The name of the fanout exchange of the signal. All processes of one system use the same name, and two systems on one
    /// broker use two names. It has 1 to 255 characters from the ASCII letters, the digits, <c>_</c>, <c>-</c>, <c>.</c>,
    /// and <c>:</c>, and does not start with <c>amq.</c>, which the broker keeps for itself. The default is
    /// <c>msghatch.dispatch</c>.
    /// </summary>
    [Required]
    [RegularExpression(@"^(?!amq\.)[A-Za-z0-9_.:-]{1,255}$")]
    public string ExchangeName { get; set; } = "msghatch.dispatch";

    /// <summary>Checks that <see cref="Uri"/> is an absolute URI with the scheme <c>amqp</c> or <c>amqps</c>.</summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>The failures.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Uri is not null && !(Uri.IsAbsoluteUri && Uri.Scheme is "amqp" or "amqps"))
        {
            yield return new ValidationResult("Uri is an absolute URI with the scheme amqp or amqps.", [nameof(Uri)]);
        }
    }
}
