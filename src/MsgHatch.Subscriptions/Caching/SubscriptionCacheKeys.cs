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

    /// <summary>The key of the subscriptions of one owner, with the owner part written in the same interpolation (D282).</summary>
    public string Key(MessageType type, SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => $"{TenantTag}:{type.Name}:d",
        SubscriptionLevel.Company => $"{TenantTag}:{type.Name}:c:{Encode(owner.Company!.Value.Value)}",
        _ => $"{TenantTag}:{type.Name}:u:{Encode(owner.User!.Value.Value)}",
    };

    public string OwnerTag(SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => $"{TenantTag}:d",
        SubscriptionLevel.Company => $"{TenantTag}:c:{Encode(owner.Company!.Value.Value)}",
        _ => $"{TenantTag}:u:{Encode(owner.User!.Value.Value)}",
    };

    private static string Encode(string id) => Uri.EscapeDataString(id);
}
