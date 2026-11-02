using System.Collections.Concurrent;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Templates;

/// <summary>
/// The template store of a folder (section 4.10). It reads the files
/// <c>{MessageType}.{Language}[.{Scope}].subject.txt</c>, <c>.body.html</c>, and <c>.body.txt</c>. The template id is
/// the full path with no part suffix, and the version is the latest last write time of the parts, in ticks. It caches
/// its sources and its misses: on each lookup, it reads only the last write times of the part files, and reads the
/// files again only when a time changed.
/// </summary>
public sealed class FileSystemTemplateStore : ITemplateStore
{
    private static readonly DateTime _missingFileTimeUtc = DateTime.FromFileTimeUtc(0);

    private readonly string _rootPath;
    private readonly ConcurrentDictionary<TemplateLookup, (long[] WriteTicks, TemplateSource? Source)> _entries = new();

    /// <summary>Makes the store of a folder.</summary>
    /// <param name="rootPath">The folder of the template files.</param>
    public FileSystemTemplateStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
    }

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct)
    {
        try
        {
            var basePath = Path.Combine(_rootPath, TemplateFiles.BaseName(lookup));
            var writeTicks = WriteTicksOf(basePath);
            if (_entries.TryGetValue(lookup, out var entry) && entry.WriteTicks.AsSpan().SequenceEqual(writeTicks))
            {
                return ErrorOr<TemplateSource?>.ForValue(entry.Source);
            }

            var source = await ReadAsync(basePath, writeTicks, ct);
            _entries[lookup] = (writeTicks, source);
            return ErrorOr<TemplateSource?>.ForValue(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Error(Errors.Unavailable, exception.Message, exception);
        }
    }

    private static long[] WriteTicksOf(string basePath) =>
        [.. TemplateFiles.Parts.Select(part => WriteTicksOfFile(basePath + part.Suffix))];

    private static long WriteTicksOfFile(string path)
    {
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        return lastWriteUtc == _missingFileTimeUtc ? 0 : lastWriteUtc.Ticks;
    }

    private static async ValueTask<TemplateSource?> ReadAsync(string basePath, long[] writeTicks, CancellationToken ct)
    {
        var parts = new List<TemplatePart>();
        for (var index = 0; index < TemplateFiles.Parts.Length; index++)
        {
            var (name, kind, suffix) = TemplateFiles.Parts[index];
            if (writeTicks[index] != 0)
            {
                parts.Add(new TemplatePart(name, kind, await File.ReadAllTextAsync(basePath + suffix, ct)));
            }
        }

        return parts.Count == 0 ? null : new TemplateSource(new TemplateKey(basePath, writeTicks.Max()), parts);
    }
}
