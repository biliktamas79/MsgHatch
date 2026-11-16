using System.Collections.Concurrent;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Placeholders;

namespace MsgHatch.Email.Templates;

/// <summary>
/// The prepared templates of one payload type (section 4.10): each <see cref="TemplateKey"/> of a part kind is parsed
/// and bound one time, and its prepared template is kept, so that a cache hit allocates nothing (section 4.11). An
/// invalid template is parsed again on each lookup, so that its error stays visible.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal static class ParsedTemplateCache<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    // ponytail: unbounded cache, add a size limit if a store produces many versions
    private static readonly ConcurrentDictionary<(TemplateKey Key, TemplatePartKind Kind), PlaceholderTemplate<TPayload>> _templates = new();

    public static ErrorOr<PlaceholderTemplate<TPayload>> GetOrParse(TemplateSource source, TemplatePartKind kind)
    {
        if (_templates.TryGetValue((source.Key, kind), out var cached))
        {
            return cached;
        }

        var parsed = TemplateParser.Parse(source, kind, TPayload.Writer);
        return parsed.IsError ? parsed.Error : _templates.GetOrAdd((source.Key, kind), new PlaceholderTemplate<TPayload>(parsed.Value));
    }
}
