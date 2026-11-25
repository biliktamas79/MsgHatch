using Microsoft.Extensions.Options;

namespace MsgHatch.Channels.Email.Resend;

/// <summary>The source-generated validator of <see cref="ResendOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class ResendOptionsValidator : IValidateOptions<ResendOptions>;
