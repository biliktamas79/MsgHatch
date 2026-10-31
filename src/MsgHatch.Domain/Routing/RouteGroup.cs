namespace MsgHatch.Domain.Routing;

/// <summary>The routes of one group, in the order of the router.</summary>
/// <typeparam name="TKey">The key of the groups.</typeparam>
/// <param name="Key">The key of the group.</param>
/// <param name="Routes">The routes of the group.</param>
/// <param name="Positions">For each route of the group, its position in the route list that the grouping got.</param>
public sealed record RouteGroup<TKey>(TKey Key, ReadOnlyMemory<MessageRoute> Routes, ReadOnlyMemory<int> Positions);
