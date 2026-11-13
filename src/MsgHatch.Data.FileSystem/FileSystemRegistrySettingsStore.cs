using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The registry settings in JSON files (section 4.20): one file for each entry, <c>{RootPath}/registry/channels/{channel}.json</c>
/// and <c>{RootPath}/registry/message-types/{type}.json</c>. A new entry is a file that the store creates only when no
/// file exists. A change opens the file with <see cref="FileShare.None"/>, checks the stored revision, and writes the
/// entry in the same open file, so of two writers only one changes it.
/// </summary>
public sealed class FileSystemRegistrySettingsStore : IRegistrySettingsStore
{
    private readonly string _channels;
    private readonly string _messageTypes;

    /// <summary>Makes the store on a folder.</summary>
    /// <param name="rootPath">The root folder of the file stores. The entries are under <c>{rootPath}/registry</c>.</param>
    public FileSystemRegistrySettingsStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = rootPath;
        _channels = Path.Combine(rootPath, "registry", "channels");
        _messageTypes = Path.Combine(rootPath, "registry", "message-types");
    }

    /// <summary>The root folder of the file stores.</summary>
    public string RootPath { get; }

    /// <inheritdoc />
    public ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => ErrorOr<RegistryEntries>.ForValue(new RegistryEntries(
            [.. ReadAll(_channels, FileStoreJsonContext.Default.ChannelEntry).OrderBy(static entry => entry.Channel.Name, StringComparer.Ordinal)],
            [.. ReadAll(_messageTypes, FileStoreJsonContext.Default.MessageTypeEntry).OrderBy(static entry => entry.Type.Name, StringComparer.Ordinal)]))));

    /// <inheritdoc />
    public ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ValueTask.FromResult(FileFailures.Run(() => Save(
            PathOf(_channels, entry.Channel.Name), entry, entry.Revision, FileStoreJsonContext.Default.ChannelEntry, static stored => stored.Revision)));
    }

    /// <inheritdoc />
    public ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ValueTask.FromResult(FileFailures.Run(() => Save(
            PathOf(_messageTypes, entry.Type.Name), entry, entry.Revision, FileStoreJsonContext.Default.MessageTypeEntry,
            static stored => stored.Revision)));
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => Delete(PathOf(_channels, channel.Name))));

    /// <inheritdoc />
    public ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => Delete(PathOf(_messageTypes, type.Name))));

    private static string PathOf(string folder, string name) => Path.Combine(folder, $"{name}.json");

    private static IEnumerable<T> ReadAll<T>(string folder, JsonTypeInfo<T> json) =>
        Directory.Exists(folder)
            ? [.. Directory.EnumerateFiles(folder, "*.json").Select(path => JsonSerializer.Deserialize(File.ReadAllBytes(path), json)
                ?? throw new JsonException($"The registry file {Path.GetFileName(path)} is the JSON null."))]
            : [];

    private static Error? Save<T>(string path, T entry, int revision, JsonTypeInfo<T> json, Func<T, int> revisionOf) =>
        revision == 1 ? Create(path, entry, json) : Replace(path, entry, revision, json, revisionOf);

    private static Error? Create<T>(string path, T entry, JsonTypeInfo<T> json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, entry, json);
            return null;
        }
        catch (IOException) when (File.Exists(path))
        {
            return Conflict(revision: 1);
        }
    }

    private static Error? Replace<T>(string path, T entry, int revision, JsonTypeInfo<T> json, Func<T, int> revisionOf)
    {
        if (!File.Exists(path))
        {
            return Conflict(revision);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var stored = revisionOf(JsonSerializer.Deserialize(stream, json) ?? throw new JsonException($"The registry file {Path.GetFileName(path)} is the JSON null."));
        if (stored != revision - 1)
        {
            return Conflict(revision);
        }

        stream.SetLength(0);
        stream.Position = 0;
        JsonSerializer.Serialize(stream, entry, json);
        return null;
    }

    private static Error? Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return null;
    }

    private static Error Conflict(int revision) => new(Errors.Conflict, $"The stored revision is not the revision before {revision}.");
}
