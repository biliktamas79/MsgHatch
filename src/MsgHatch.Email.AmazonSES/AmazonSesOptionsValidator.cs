using Microsoft.Extensions.Options;

namespace MsgHatch.Email.AmazonSES;

/// <summary>The source-generated validator of <see cref="AmazonSesOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class AmazonSesOptionsValidator : IValidateOptions<AmazonSesOptions>;
