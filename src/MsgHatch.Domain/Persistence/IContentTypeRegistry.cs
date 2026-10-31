using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Persistence;

/// <summary>
/// Gives the stores the type information of each persisted type (section 4.5). The entries come from the
/// registrations, and are read-only for the stores.
/// </summary>
public interface IContentTypeRegistry
{
    /// <summary>Each registered entry.</summary>
    IReadOnlyList<ContentTypeEntry> Entries { get; }

    /// <summary>Finds the entry of one version of a content type.</summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="version">The version.</param>
    /// <param name="entry">The entry, when it is found.</param>
    /// <returns>True when the entry is found.</returns>
    bool TryFind(string contentType, int version, [MaybeNullWhen(false)] out ContentTypeEntry entry);
}
