using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace WolverineExample.Contracts;

/// <summary>The RabbitMQ broker of the publisher and the consumer, from the section <c>RabbitMq</c>.</summary>
public sealed class RabbitMqOptions
{
    /// <summary>The configuration section of the options.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>The queue of the domain events. The publisher sends to it, and the consumer listens to it.</summary>
    public const string EventQueue = "msghatch-example-events";

    /// <summary>The AMQP URI of the broker, for example <c>amqp://127.0.0.1:5672</c>.</summary>
    [Required]
    public Uri? Uri { get; set; }

    /// <summary>
    /// Binds the options from the configuration, and validates them. Wolverine reads them before the host builds its
    /// services, so the options pattern of the host cannot give them.
    /// </summary>
    /// <param name="configuration">The configuration of the host.</param>
    /// <returns>The valid options.</returns>
    /// <exception cref="OptionsValidationException">The options are not valid.</exception>
    public static RabbitMqOptions Load(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        var result = new RabbitMqOptionsValidator().Validate(Options.DefaultName, options);
        return result.Failed ? throw new OptionsValidationException(Options.DefaultName, typeof(RabbitMqOptions), result.Failures) : options;
    }
}

/// <summary>The source-generated validator of <see cref="RabbitMqOptions"/>.</summary>
[OptionsValidator]
public sealed partial class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>;
