using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.Delivery;

/// <summary>The source-generated validator of <see cref="RetryOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class RetryOptionsValidator : IValidateOptions<RetryOptions>;
