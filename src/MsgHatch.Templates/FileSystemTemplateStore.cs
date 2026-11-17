using System.Collections.Concurrent;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

/// <summary>
/// The template store of a folder (section 4.10). It reads the file <c>[{Scope}/]{MessageType}.{Language}.{Part}.txt</c>
/// or <c>.html</c> of a lookup (D214), with the scope folder of <see cref="ScopeFolders.FolderOf"/>. A scope with no
/// folder gives a miss. The template id is the full path of the file, and the version is its last write time, in ticks.
/// It caches its sources: on each lookup, it reads only the last write time of the file, and reads the file again only
/// when the time changed. It does not cache a miss, because a missing file has no read to skip. As a writer (D218,
/// D223), it writes a temporary file in the folder of the template, and moves it over the file. A save gives a last
/// write time after the version before the save, so each save gives a new <see cref="TemplateKey"/>.
/// </summary>
public sealed class FileSystemTemplateStore : ITemplateStore, ITemplateWriter
{
    private static readonly DateTime _missingFileTimeUtc = DateTime.FromFileTimeUtc(0);

    private readonly string _rootPath;
    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource> _sources = new();

    // ponytail: one write lock for each store, and two processes on one folder can race between the check and the move; a lock file if two operators write one part at the same time
    private readonly Lock _writeLock = new();

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
            return Unavailable(exception);
        }
    }

    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> ListAsync(TemplateQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            IReadOnlyList<TemplateEntry> entries =
            [
                .. FoldersOf(query.Scope)
                    .SelectMany(static folder => EntriesOf(folder.Path, folder.Scope))
                    .Where(entry => query.Type is not { } type || entry.Lookup.Type == type)
                    .OrderBy(static entry => entry.Lookup.Scope, StringComparer.Ordinal)
                    .ThenBy(static entry => entry.Lookup.Language, StringComparer.Ordinal)
                    .ThenBy(static entry => entry.Lookup.Part, StringComparer.Ordinal)
                    .ThenBy(static entry => entry.Lookup.Kind),
            ];
            return ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForValue(entries));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult<ErrorOr<IReadOnlyList<TemplateEntry>>>(Unavailable(exception));
        }
    }

    /// <inheritdoc/>
    public ValueTask<ErrorOr<TemplateEntry>> SaveAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();
        var path = TemplateFiles.PathOf(_rootPath, lookup);
        if (path is null)
        {
            return ValueTask.FromResult<ErrorOr<TemplateEntry>>(new Error(Errors.ValidationFailed, "An empty template scope has no folder."));
        }

        try
        {
            lock (_writeLock)
            {
                return ValueTask.FromResult(Save(lookup, path, text, expectedVersion));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult<ErrorOr<TemplateEntry>>(Unavailable(exception));
        }
    }

    /// <inheritdoc/>
    public ValueTask<Error?> DeleteAsync(TemplateLookup lookup, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = TemplateFiles.PathOf(_rootPath, lookup);
        try
        {
            lock (_writeLock)
            {
                if (path is not null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return ValueTask.FromResult<Error?>(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult<Error?>(Unavailable(exception));
        }
    }

    private static ErrorOr<TemplateEntry> Save(TemplateLookup lookup, string path, string text, long expectedVersion)
    {
        var storedVersion = WriteTicksOf(path);
        if (storedVersion != expectedVersion)
        {
            return new Error(Errors.Conflict, $"The stored version of the template is {storedVersion}, not {expectedVersion}.");
        }

        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        var temporaryPath = Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, text);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return new TemplateEntry(lookup, VersionAfter(path, storedVersion));
    }

    private static long VersionAfter(string path, long storedVersion)
    {
        var version = WriteTicksOf(path);
        if (version > storedVersion)
        {
            return version;
        }

        File.SetLastWriteTimeUtc(path, new DateTime(storedVersion + 1, DateTimeKind.Utc));
        return storedVersion + 1;
    }

    private IEnumerable<(string Path, string? Scope)> FoldersOf(string? scope)
    {
        if (!Directory.Exists(_rootPath))
        {
            yield break;
        }

        if (scope is not null)
        {
            if (ScopeFolders.FolderOf(scope) is { } folder && Directory.Exists(Path.Combine(_rootPath, folder)))
            {
                yield return (Path.Combine(_rootPath, folder), scope);
            }

            yield break;
        }

        yield return (_rootPath, null);
        foreach (var directory in Directory.EnumerateDirectories(_rootPath))
        {
            if (ScopeFolders.ScopeOf(Path.GetFileName(directory)) is { } directoryScope)
            {
                yield return (directory, directoryScope);
            }
        }
    }

    private static IEnumerable<TemplateEntry> EntriesOf(string folder, string? scope) =>
        Directory.EnumerateFiles(folder)
            .Select(path => (Path: path, Lookup: TemplateFiles.LookupOf(Path.GetFileName(path), scope)))
            .Where(static file => file.Lookup is not null)
            .Select(static file => new TemplateEntry(file.Lookup!.Value, WriteTicksOf(file.Path)))
            .Where(static entry => entry.Version != 0);

    private static Error Unavailable(Exception exception) => new(Errors.Unavailable, exception.Message, exception);

    private static long WriteTicksOf(string path)
    {
        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        return lastWriteUtc == _missingFileTimeUtc ? 0 : lastWriteUtc.Ticks;
    }
}
