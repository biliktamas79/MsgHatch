using System.ComponentModel;

namespace MsgHatch.Subscriptions.Caching.Hybrid;

/// <summary>
/// One entry of <see cref="HybridCacheSubscriptionBackend"/>: the subscriptions of one message type and one owner. The
/// type is sealed and immutable, so HybridCache keeps the instance in memory with no serializer, and gives it to each
/// read with no copy (D152). A distributed second level of HybridCache needs a serializer of this type, which the
/// consumer registers in HybridCache.
/// </summary>
/// <param name="Subscriptions">The subscriptions. Nobody changes the list after the entry exists.</param>
[ImmutableObject(true)]
public sealed record HybridCacheSubscriptionEntry(IReadOnlyList<Subscription> Subscriptions);
