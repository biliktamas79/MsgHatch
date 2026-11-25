using Microsoft.Extensions.Options;

namespace MsgHatch.Channels.Broker.AmazonSns;

/// <summary>The source-generated validator of <see cref="AmazonSnsOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class AmazonSnsOptionsValidator : IValidateOptions<AmazonSnsOptions>;
