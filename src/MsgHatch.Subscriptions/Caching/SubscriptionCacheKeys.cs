using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>
/// The cache keys and tags of one tenant key (section 4.15). Each id is percent-encoded, so it holds no <c>:</c>, and
/// two tenants never share a key or a tag (D114, D153).
/// </summary>
internal sealed class SubscriptionCacheKeys
{
    private SubscriptionCacheKeys(string keyPrefix, TenantKey key)
    {
        TenantTag = key.Tenant is { } tenant ? $"{keyPrefix}:t:{Encode(tenant.Value)}" : $"{keyPrefix}:n";
    }

    /// <summary>The tag of each entry of the tenant.</summary>
    public string TenantTag { get; }

    public static SubscriptionCacheKeys For(IServiceProvider services, TenantKey key) =>
        new(services.GetRequiredService<IOptions<SubscriptionCacheOptions>>().Value.KeyPrefix, key);

    public string Key(MessageType type, SubscriptionOwner owner) => $"{TenantTag}:{type.Name}:{OwnerPart(owner)}";

    public string OwnerTag(SubscriptionOwner owner) => $"{TenantTag}:{OwnerPart(owner)}";

    private static string OwnerPart(SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => "d",
        SubscriptionLevel.Company => $"c:{Encode(owner.Company!.Value.Value)}",
        _ => $"u:{Encode(owner.User!.Value.Value)}",
    };

    private static string Encode(string id) => Uri.EscapeDataString(id);
}
