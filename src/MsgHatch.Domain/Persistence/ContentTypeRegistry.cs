using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace MsgHatch.Domain.Persistence;

/// <summary>The content type registry that the registrations fill. The first entry of a content type and version wins.</summary>
internal sealed class ContentTypeRegistry : IContentTypeRegistry
{
    private readonly FrozenDictionary<(string ContentType, int Version), ContentTypeEntry> _entries;

    public ContentTypeRegistry(IEnumerable<ContentTypeEntry> entries)
    {
        Entries = [.. entries];
        _entries = Entries
            .DistinctBy(static entry => (entry.ContentType, entry.Version))
            .ToFrozenDictionary(static entry => (entry.ContentType, entry.Version));
    }

    public IReadOnlyList<ContentTypeEntry> Entries { get; }

    public static ContentTypeEntry Entry(string contentType, int version, Type clrType, JsonTypeInfo json)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(json);
        return new ContentTypeEntry(contentType, version, $"{contentType}@{version}", clrType, json);
    }

    public bool TryFind(string contentType, int version, [MaybeNullWhen(false)] out ContentTypeEntry entry) =>
        _entries.TryGetValue((contentType, version), out entry);
}
