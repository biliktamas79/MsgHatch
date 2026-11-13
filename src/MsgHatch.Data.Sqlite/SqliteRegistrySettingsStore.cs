using System.Text.Json;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The registry settings in SQLite (section 4.20): one row of <c>MsgHatchChannelEntry</c> for each channel entry, and
/// one row of <c>MsgHatchMessageTypeEntry</c> for each message type entry, with <c>DisabledChannels</c> as a JSON array.
/// A new entry is an <c>INSERT OR IGNORE</c>, and a change is an <c>UPDATE</c> with the expected revision. A changed
/// row count of 0 gives <see cref="Errors.Conflict"/>. The store shares the database and the schema of the inbox and
/// the outbox.
/// </summary>
public sealed class SqliteRegistrySettingsStore : IRegistrySettingsStore
{
    private const string _channelColumns = "Channel, DisplayName, Description, Enabled, Revision, LastModifiedAtUtc";

    private const string _messageTypeColumns = "Type, DisplayName, Description, Enabled, DisabledChannels, Revision, LastModifiedAtUtc";

    private const string _insertChannel = $"""
        INSERT OR IGNORE INTO MsgHatchChannelEntry ({_channelColumns})
        VALUES (@name, @displayName, @description, @enabled, @revision, @lastModifiedAtUtc)
        """;

    private const string _updateChannel = """
        UPDATE MsgHatchChannelEntry SET
            DisplayName = @displayName, Description = @description, Enabled = @enabled, Revision = @revision,
            LastModifiedAtUtc = @lastModifiedAtUtc
        WHERE Channel = @name AND Revision = @revision - 1
        """;

    private const string _insertMessageType = $"""
        INSERT OR IGNORE INTO MsgHatchMessageTypeEntry ({_messageTypeColumns})
        VALUES (@name, @displayName, @description, @enabled, @disabledChannels, @revision, @lastModifiedAtUtc)
        """;

    private const string _updateMessageType = """
        UPDATE MsgHatchMessageTypeEntry SET
            DisplayName = @displayName, Description = @description, Enabled = @enabled, DisabledChannels = @disabledChannels,
            Revision = @revision, LastModifiedAtUtc = @lastModifiedAtUtc
        WHERE Type = @name AND Revision = @revision - 1
        """;

    private static readonly SqliteSchema _schema = SqliteSchema.Core;

    /// <summary>Makes the store on a database.</summary>
    /// <param name="connectionString">The connection string of the database, for example <c>Data Source=msghatch.db</c>.</param>
    public SqliteRegistrySettingsStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ConnectionString = connectionString;
    }

    /// <summary>The connection string of the database.</summary>
    public string ConnectionString { get; }

    /// <inheritdoc />
    public ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() =>
        {
            using var connection = _schema.Open(ConnectionString);
            return ErrorOr<RegistryEntries>.ForValue(new RegistryEntries(
                ReadAll(connection, $"SELECT {_channelColumns} FROM MsgHatchChannelEntry ORDER BY Channel", ChannelEntryOf),
                ReadAll(connection, $"SELECT {_messageTypeColumns} FROM MsgHatchMessageTypeEntry ORDER BY Type", MessageTypeEntryOf)));
        }));

    /// <inheritdoc />
    public ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ValueTask.FromResult(Save(
            entry.Revision == 1 ? _insertChannel : _updateChannel,
            command => WithEntry(command, entry.Channel.Name, entry.DisplayName, entry.Description, entry.Enabled, entry.Revision, entry.LastModifiedAtUtc)));
    }

    /// <inheritdoc />
    public ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ValueTask.FromResult(Save(
            entry.Revision == 1 ? _insertMessageType : _updateMessageType,
            command => WithEntry(command, entry.Type.Name, entry.DisplayName, entry.Description, entry.Enabled, entry.Revision, entry.LastModifiedAtUtc)
                .With("@disabledChannels", ChannelNamesJson(entry.DisabledChannels))));
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct) =>
        ValueTask.FromResult(Delete("DELETE FROM MsgHatchChannelEntry WHERE Channel = @name", channel.Name));

    /// <inheritdoc />
    public ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct) =>
        ValueTask.FromResult(Delete("DELETE FROM MsgHatchMessageTypeEntry WHERE Type = @name", type.Name));

    private Error? Save(string sql, Func<SqliteCommand, SqliteCommand> withValues) =>
        SqliteFailures.Run(() =>
        {
            using var connection = _schema.Open(ConnectionString);
            using var command = withValues(connection.Command(sql));
            return command.ExecuteNonQuery() == 1
                ? null
                : new Error(Errors.Conflict, "The stored revision is not the revision before the revision of the entry.");
        });

    private Error? Delete(string sql, string name) =>
        SqliteFailures.Run(() =>
        {
            using var connection = _schema.Open(ConnectionString);
            using var command = connection.Command(sql).With("@name", name);
            command.ExecuteNonQuery();
            return null;
        });

    private static List<T> ReadAll<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> entryOf)
    {
        using var command = connection.Command(sql);
        using var reader = command.ExecuteReader();
        var entries = new List<T>();
        while (reader.Read())
        {
            entries.Add(entryOf(reader));
        }

        return entries;
    }

    private static ChannelEntry ChannelEntryOf(SqliteDataReader row) =>
        new(new ChannelId(row.GetString(0)), TextOrNull(row, 1), TextOrNull(row, 2), row.GetInt64(3) != 0, row.GetInt32(4),
            SqliteValues.ParseTime(row.GetString(5)));

    private static MessageTypeEntry MessageTypeEntryOf(SqliteDataReader row) =>
        new(new MessageType(row.GetString(0)), TextOrNull(row, 1), TextOrNull(row, 2), row.GetInt64(3) != 0,
            ChannelsOf(row.GetString(4)), row.GetInt32(5), SqliteValues.ParseTime(row.GetString(6)));

    private static SqliteCommand WithEntry(
        SqliteCommand command, string name, string? displayName, string? description, bool enabled, int revision,
        DateTime lastModifiedAtUtc) => command
        .With("@name", name)
        .With("@displayName", displayName)
        .With("@description", description)
        .With("@enabled", enabled ? 1 : 0)
        .With("@revision", revision)
        .With("@lastModifiedAtUtc", SqliteValues.Time(lastModifiedAtUtc));

    private static string? TextOrNull(SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetString(column);

    /// <summary>The channel names as a JSON array. A channel name has only ASCII letters, digits, and <c>_</c>, so it has no character to escape.</summary>
    private static string ChannelNamesJson(IReadOnlyList<ChannelId> channels) =>
        $"[{string.Join(',', channels.Select(static channel => $"\"{channel.Name}\""))}]";

    private static ChannelId[] ChannelsOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(static element => new ChannelId(element.GetString()!))];
    }
}
