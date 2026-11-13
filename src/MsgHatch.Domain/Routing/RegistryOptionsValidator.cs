using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.Routing;

/// <summary>The source-generated validator of <see cref="RegistryOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class RegistryOptionsValidator : IValidateOptions<RegistryOptions>;
