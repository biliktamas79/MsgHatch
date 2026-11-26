# MsgHatch.Subscriptions.Caching.FusionCache

The FusionCache backend of the subscription cache of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). The cache keeps the subscriptions of one owner and message type, so the subscription router reads the store less often. A write through the subscription store removes the entries of its owner. Each cache key carries the tenant.

With the cache on, a change that does not go through the subscription store, for example a direct database update, takes effect only after `ISubscriptionCache.InvalidateAsync` or after the expiration. With many instances, an invalidation reaches the other instances only through a FusionCache backplane.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Caching.FusionCache
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseSubscriptions()
    .UseInMemorySubscriptionStore()
    .UseFusionSubscriptionCache();
```

`UseFusionSubscriptionCache` registers a named FusionCache of its own, so the FusionCache instances of the host do not change. Its second argument configures that FusionCache, for example with a distributed cache and a backplane. It binds `SubscriptionCacheOptions` from the section `SubscriptionCache` under the root of `AddMsgHatch`, by default `MsgHatch:SubscriptionCache`, and a value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseSubscriptions()
    .UseInMemorySubscriptionStore()
    .UseFusionSubscriptionCache(o => o.Expiration = TimeSpan.FromMinutes(5));
```

## Options

[//]: # (options: SubscriptionCacheOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `Expiration` | `TimeSpan` | `00:01:00` | 1 second to 1 day. The time that the cache keeps the subscriptions of one owner and message type. |
| `KeyPrefix` | `string` | `msghatch:sub` | Required. The start of each cache key and tag. Only ASCII letters, digits, `:`, `_`, and `-`. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "SubscriptionCache": {
      "Expiration": "00:01:00",
      "KeyPrefix": "msghatch:sub"
    }
  }
}
```

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `ZiggyCreatures.FusionCache` within the major 2 at the time of the pack, not on 2.9.0. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the FusionCache backend of the subscription cache with `UseFusionSubscriptionCache`.
