using Microsoft.Extensions.Options;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>The source-generated validator of <see cref="NamedPipeSignalOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class NamedPipeSignalOptionsValidator : IValidateOptions<NamedPipeSignalOptions>;
