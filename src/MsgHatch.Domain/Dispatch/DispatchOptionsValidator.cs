using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.Dispatch;

/// <summary>The source-generated validator of <see cref="DispatchOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class DispatchOptionsValidator : IValidateOptions<DispatchOptions>;
