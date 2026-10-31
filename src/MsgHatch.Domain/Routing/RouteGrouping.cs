using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// Groups the routes of one event. Each group keeps the order of the router, and the groups come in the order of
/// their first route.
/// </summary>
public static class RouteGrouping
{
    /// <summary>Splits the routes by channel.</summary>
    /// <param name="routes">The routes.</param>
    /// <returns>One group for each channel. An empty route list gives no groups.</returns>
    public static IReadOnlyList<RouteGroup<ChannelId>> ByChannel(ReadOnlyMemory<MessageRoute> routes) =>
        Group(routes, static route => route.Channel, EqualityComparer<ChannelId>.Default);

    /// <summary>
    /// Groups the routes by language and template scope. Two languages that differ only in case are one language.
    /// </summary>
    /// <param name="routes">The routes.</param>
    /// <returns>One group for each language and template scope. An empty route list gives no groups.</returns>
    public static IReadOnlyList<RouteGroup<(string Language, string? TemplateScope)>> ByLanguageAndScope(
        ReadOnlyMemory<MessageRoute> routes) =>
        Group(routes, static route => (route.Language, route.TemplateScope), LanguageAndScopeComparer.Instance);

    private static RouteGroup<TKey>[] Group<TKey>(
        ReadOnlyMemory<MessageRoute> routes, Func<MessageRoute, TKey> keyOf, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var span = routes.Span;
        var positionsByKey = new Dictionary<TKey, List<int>>(comparer);
        var keys = new List<TKey>();
        for (var position = 0; position < span.Length; position++)
        {
            var key = keyOf(span[position]);
            if (!positionsByKey.TryGetValue(key, out var positions))
            {
                positions = [];
                positionsByKey.Add(key, positions);
                keys.Add(key);
            }

            positions.Add(position);
        }

        return [.. keys.Select(key => ToGroup(key, positionsByKey[key], routes))];
    }

    private static RouteGroup<TKey> ToGroup<TKey>(TKey key, List<int> positions, ReadOnlyMemory<MessageRoute> routes)
    {
        var span = routes.Span;
        var grouped = new MessageRoute[positions.Count];
        for (var index = 0; index < grouped.Length; index++)
        {
            grouped[index] = span[positions[index]];
        }

        return new RouteGroup<TKey>(key, grouped, positions.ToArray());
    }

    private sealed class LanguageAndScopeComparer : IEqualityComparer<(string Language, string? TemplateScope)>
    {
        public static readonly LanguageAndScopeComparer Instance = new();

        public bool Equals((string Language, string? TemplateScope) x, (string Language, string? TemplateScope) y) =>
            string.Equals(x.Language, y.Language, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.TemplateScope, y.TemplateScope, StringComparison.Ordinal);

        public int GetHashCode((string Language, string? TemplateScope) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Language ?? string.Empty),
                StringComparer.Ordinal.GetHashCode(obj.TemplateScope ?? string.Empty));
    }
}
