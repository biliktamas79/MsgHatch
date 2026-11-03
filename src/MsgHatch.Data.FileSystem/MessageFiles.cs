using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The folder helper of the two file stores (section 4.5, D26). A message folder has the status folders
/// <c>pending</c>, <c>claimed</c>, <c>done</c>, and <c>failed</c>. A file name is
/// <c>{dueAtUtc:yyyyMMddHHmmssfff}_{sequence}_{key}.json</c>. The sequence counts the files of one instance with the
/// same due time, so a sort by name keeps the insert order of the messages with the same due time. A marker
/// <c>ids/{tenantFolder}/{id}</c> under the root of a store records each stored key.
/// </summary>
internal sealed class MessageFiles
{
    private const string _pending = "pending";
    private const string _claimed = "claimed";
    private const string _timeFormat = "yyyyMMddHHmmssfff";
    private const string _jsonPattern = "*.json";
    private static readonly string[] _dueFolders = [_pending, _claimed];
    private static readonly string[] _statusFolders = [_pending, _claimed, "done", "failed"];

    private readonly Lock _lock = new();
    private readonly Lock _claimLock = new();
    private string _lastTime = string.Empty;
    private int _sequence;

    /// <summary>
    /// Adds a message file to <c>pending</c> when its key has no marker yet. The file goes to a temporary file first.
    /// Then the marker is created with <see cref="FileMode.CreateNew"/>, and a rename gives the file its name.
    /// </summary>
    /// <returns>False when the key is already stored.</returns>
    // ponytail: a crash between the marker and the rename loses the message, use a database store when a crash must not lose one
    public bool Add(string marker, string folder, DateTime dueAtUtc, string key, byte[] content)
    {
        var path = Path.Combine(folder, _pending, NewName(dueAtUtc, key));
        var temporary = WriteTemporary(path, content);
        if (!TryCreateMarker(marker))
        {
            File.Delete(temporary);
            return false;
        }

        File.Move(temporary, path);
        return true;
    }

    /// <summary>
    /// Moves the due files of the folders to <c>claimed</c>, in the order of their names, with the lease end as their
    /// new due time (D103). A file is due in <c>pending</c> at its due time, and in <c>claimed</c> when its lease ends.
    /// The first caller that moves a file wins, and the others skip that file. The claims of one instance take turns.
    /// </summary>
    /// <returns>The new paths of the claimed files.</returns>
    // ponytail: Windows renames through a handle, so two processes can rarely claim one file in the same lease, use a database store for exact claims
    public IReadOnlyList<string> ClaimDue(IReadOnlyList<string> folders, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        var claimed = new List<string>();
        lock (_claimLock)
        {
            foreach (var file in DueFiles(folders, nowUtc))
            {
                var target = Path.Combine(file.Folder, _claimed, WithDueAt(Path.GetFileName(file.Path), leaseUntilUtc));
                if (TryMove(file.Path, target))
                {
                    claimed.Add(target);
                }

                if (claimed.Count == maxCount)
                {
                    break;
                }
            }
        }

        return claimed;
    }

    /// <summary>The earliest due time in <c>pending</c> and <c>claimed</c> of the folders, so it includes the lease ends.</summary>
    public static DateTime? NextDueAtUtc(IReadOnlyList<string> folders) =>
        DueFolderFiles(folders).Select(static file => (DateTime?)file.DueAtUtc).Min();

    /// <summary>Finds the file of a key in the status folders of one folder.</summary>
    public static string? Find(string folder, string key) =>
        _statusFolders.SelectMany(status => FilesOf(Path.Combine(folder, status), $"*_{key}.json")).FirstOrDefault();

    /// <summary>The key part of a file name: the id, and for a tenant also the name of the tenant.</summary>
    public static string KeyOf(Guid id, TenantId? tenant) =>
        tenant is { } value ? $"{id:D}_{NameOf(value)}" : id.ToString("D");

    /// <summary>The marker <c>ids/{tenantFolder}/{id}</c> of a stored key under the root of a store.</summary>
    public static string MarkerOf(string root, TenantId? tenant, Guid id) =>
        tenant is { } value
            ? Path.Combine(root, "ids", "tenants", NameOf(value), id.ToString("D"))
            : Path.Combine(root, "ids", "no-tenant", id.ToString("D"));

    /// <summary>
    /// The tenant id in a file or folder name: escaped with <see cref="Uri.EscapeDataString(string)"/>, which also escapes
    /// <c>^</c>, and each capital letter A to Z as <c>^</c> and the small letter. Windows and macOS ignore the case of a
    /// name, but a tenant id is case-sensitive.
    /// </summary>
    private static string NameOf(TenantId tenant) =>
        string.Concat(Uri.EscapeDataString(tenant.Value).Select(static character =>
            char.IsAsciiLetterUpper(character) ? $"^{char.ToLowerInvariant(character)}" : character.ToString()));

    /// <summary>
    /// Writes the new content of a file into the folder of its status, with its next try time as the due time, through
    /// a temporary file and a rename. Then it deletes the old file when the path changed, so a reader never sees half a file.
    /// </summary>
    public static void Replace(string path, DeliveryState delivery, byte[] content)
    {
        var target = Path.Combine(
            FolderOf(path), StatusFolderOf(delivery.Status), WithDueAt(Path.GetFileName(path), delivery.NextTryAtUtc));
        File.Move(WriteTemporary(target, content), target, overwrite: true);
        if (!string.Equals(target, path, StringComparison.Ordinal))
        {
            File.Delete(path);
        }
    }

    /// <summary>Writes the new content of a file in its place, through a temporary file and a rename.</summary>
    public static void Overwrite(string path, byte[] content) => File.Move(WriteTemporary(path, content), path, overwrite: true);

    /// <summary>Moves a file into <c>failed</c> unchanged, for example a file that the code cannot read.</summary>
    public static void MoveToFailed(string path)
    {
        var target = Path.Combine(FolderOf(path), StatusFolderOf(DeliveryStatus.Failed), Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target, overwrite: true);
    }

    /// <summary>Deletes a root folder with its files.</summary>
    /// <returns>The number of message files that it deleted.</returns>
    public static int DeleteRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var count = Directory.EnumerateFiles(root, _jsonPattern, SearchOption.AllDirectories).Count();
        Directory.Delete(root, recursive: true);
        return count;
    }

    private string NewName(DateTime dueAtUtc, string key)
    {
        var time = TimeOf(dueAtUtc);
        lock (_lock)
        {
            _sequence = string.Equals(time, _lastTime, StringComparison.Ordinal) ? _sequence + 1 : 0;
            _lastTime = time;
            return $"{time}_{_sequence.ToString("D10", CultureInfo.InvariantCulture)}_{key}.json";
        }
    }

    private static IEnumerable<DueFile> DueFiles(IReadOnlyList<string> folders, DateTime nowUtc) =>
        DueFolderFiles(folders)
            .Where(file => file.DueAtUtc <= nowUtc)
            .OrderBy(static file => Path.GetFileName(file.Path), StringComparer.Ordinal);

    private static IEnumerable<DueFile> DueFolderFiles(IReadOnlyList<string> folders) =>
        from folder in folders
        from status in _dueFolders
        from path in FilesOf(Path.Combine(folder, status), _jsonPattern)
        let dueAtUtc = DueAtOf(path)
        where dueAtUtc is not null
        select new DueFile(folder, path, dueAtUtc.Value);

    private static IEnumerable<string> FilesOf(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern) : [];

    private static bool TryMove(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            File.Move(path, target);
            return true;
        }
        catch (Exception exception) when (IsLostRace(exception, path))
        {
            return false;
        }
    }

    /// <summary>
    /// Windows gives <see cref="UnauthorizedAccessException"/> for a rename that meets a file that another caller
    /// replaces at the same time. With the source gone, another caller claimed the file.
    /// </summary>
    private static bool IsLostRace(Exception exception, string path) =>
        exception is IOException || (exception is UnauthorizedAccessException && !File.Exists(path));

    private static bool TryCreateMarker(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    private static string WriteTemporary(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, content);
        return temporary;
    }

    private static string FolderOf(string path) => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    private static string StatusFolderOf(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Done => "done",
        DeliveryStatus.Failed => "failed",
        _ => _pending,
    };

    private static string WithDueAt(string name, DateTime dueAtUtc) => string.Concat(TimeOf(dueAtUtc), name.AsSpan(_timeFormat.Length));

    private static DateTime? DueAtOf(string path)
    {
        var name = Path.GetFileName(path.AsSpan());
        return name.Length >= _timeFormat.Length
            && DateTime.TryParseExact(
                name[.._timeFormat.Length], _timeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dueAtUtc)
            ? dueAtUtc
            : null;
    }

    private static string TimeOf(DateTime dueAtUtc) =>
        new DateTime(RoundUpToMillisecond(dueAtUtc.Ticks), DateTimeKind.Utc).ToString(_timeFormat, CultureInfo.InvariantCulture);

    private static long RoundUpToMillisecond(long ticks) =>
        (ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;

    private readonly record struct DueFile(string Folder, string Path, DateTime DueAtUtc);
}
