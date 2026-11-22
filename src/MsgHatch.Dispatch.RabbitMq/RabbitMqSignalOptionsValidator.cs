using Microsoft.Extensions.Options;

namespace MsgHatch.Dispatch.RabbitMq;

/// <summary>The source-generated validator of <see cref="RabbitMqSignalOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class RabbitMqSignalOptionsValidator : IValidateOptions<RabbitMqSignalOptions>;
