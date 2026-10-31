using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The source-generated validator of <see cref="MsgHatchOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class MsgHatchOptionsValidator : IValidateOptions<MsgHatchOptions>;
