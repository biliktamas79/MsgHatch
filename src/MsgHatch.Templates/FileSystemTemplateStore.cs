using System.Collections.Concurrent;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

/// <summary>
/// The template store of a folder (section 4.10). It reads the file <c>[{Scope}/]{MessageType}.{Language}.{Part}.txt</c>
/// or <c>.html</c> of a lookup (D214), with the scope folder of <see cref="ScopeFolders.FolderOf"/>. A scope with no
/// folder gives a miss. The template id is the full path of the file, and the version is its last write time, in ticks.
/// It caches its sources: on each lookup, it reads only the last write time of the file, and reads the file again only
/// when the time changed. It does not cache a miss, because a missing file has no read to skip.
/// </summary>
public sealed class FileSystemTemplateStore : ITemplateStore
{
    private static readonly DateTime _missingFileTimeUtc = DateTime.FromFileTimeUtc(0);

    private readonly string _rootPath;
    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource> _sources = new();

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
            var path = TemplateFiles.PathOf(_rootPath, lookup);
            if (path is null)
            {
                return ErrorOr<TemplateSource?>.ForValue(null);
            }

            var writeTicks = WriteTicksOf(path);
            if (writeTicks == 0)
            {
                return ErrorOr<TemplateSource?>.ForValue(null);
            }

            if (_sources.TryGetValue(lookup, out var cached) && cached.Key.Version == writeTicks)
            {
                return ErrorOr<TemplateSource?>.ForValue(cached);
            }

            var source = new TemplateSource(new TemplateKey(path, writeTicks), await File.ReadAllTextAsync(path, ct));
            _sources[lookup] = source;
            return ErrorOr<TemplateSource?>.ForValue(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Error(Errors.Unavailable, exception.Message, exception);
        }
    }

    private static long WriteTicksOf(string path)
    {
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        return lastWriteUtc == _missingFileTimeUtc ? 0 : lastWriteUtc.Ticks;
    }
}
