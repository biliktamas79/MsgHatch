using System.Collections.Concurrent;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Placeholders;

namespace MsgHatch.Email.Templates;

/// <summary>
/// The prepared templates of one payload type (section 4.10): each <see cref="TemplateKey"/> is parsed and bound one
/// time, and its prepared template is kept, so that a cache hit allocates nothing (section 4.11). An invalid template
/// is parsed again on each lookup, so that its error stays visible.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal static class ParsedTemplateCache<TPayload>
    where TPayload : IPlaceholderPayload<TPayload>
{
    // ponytail: unbounded cache, add a size limit if a store produces many versions
    private static readonly ConcurrentDictionary<TemplateKey, PlaceholderEmailTemplate<TPayload>> _templates = new();

    public static ErrorOr<PlaceholderEmailTemplate<TPayload>> GetOrParse(TemplateSource source)
    {
        if (_templates.TryGetValue(source.Key, out var cached))
        {
            return cached;
        }

        var parsed = TemplateParser.Parse(source, TPayload.Writer);
        return parsed.IsError ? parsed.Error : _templates.GetOrAdd(source.Key, new PlaceholderEmailTemplate<TPayload>(parsed.Value));
    }
}
