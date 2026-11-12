using Microsoft.Extensions.Options;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>The source-generated validator of <see cref="SubscriptionCacheOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class SubscriptionCacheOptionsValidator : IValidateOptions<SubscriptionCacheOptions>;
