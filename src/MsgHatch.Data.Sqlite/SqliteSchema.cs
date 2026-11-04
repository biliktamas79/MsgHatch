using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The schema of the SQLite stores (section 4.5, D55, D56). The first connection to a database in the process sets the
/// WAL journal and creates the tables, one time for each connection string. The table and column names are the C#
/// property names, and the schema version is 1.
/// </summary>
internal static class SqliteSchema
{
    private const string _create = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS MsgHatchSchema (Version INTEGER);
        INSERT INTO MsgHatchSchema (Version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM MsgHatchSchema);
        CREATE TABLE IF NOT EXISTS MsgHatchInbox (
            Sequence INTEGER PRIMARY KEY, TenantId TEXT, Id TEXT, Status TEXT, TryCount INTEGER, MaxTryCount INTEGER,
            CreatedAtUtc TEXT, LastTryAtUtc TEXT, NextTryAtUtc TEXT, LeaseUntilUtc TEXT, ExpiresAtUtc TEXT, LastError TEXT,
            SchemaVersion INTEGER, ContentType TEXT, ContentVersion INTEGER, Flags TEXT, TraceParent TEXT, Content TEXT,
            UNIQUE (TenantId, Id));
        CREATE INDEX IF NOT EXISTS IX_MsgHatchInbox_Due ON MsgHatchInbox (NextTryAtUtc) WHERE Status = 'Pending';
        CREATE INDEX IF NOT EXISTS IX_MsgHatchInbox_Flags ON MsgHatchInbox (Flags);
        CREATE TABLE IF NOT EXISTS MsgHatchOutbox (
            Sequence INTEGER PRIMARY KEY, Id TEXT UNIQUE, TenantId TEXT, Status TEXT, TryCount INTEGER, MaxTryCount INTEGER,
            CreatedAtUtc TEXT, LastTryAtUtc TEXT, NextTryAtUtc TEXT, LeaseUntilUtc TEXT, ExpiresAtUtc TEXT, LastError TEXT,
            SchemaVersion INTEGER, SourceMessageId TEXT, MessageType TEXT, Channel TEXT, TargetKind TEXT, TargetId TEXT,
            ContentType TEXT, ContentVersion INTEGER, Flags TEXT, TraceParent TEXT, TraceType TEXT, Trace TEXT,
            Content TEXT, Tries TEXT DEFAULT '[]');
        CREATE INDEX IF NOT EXISTS IX_MsgHatchOutbox_Due ON MsgHatchOutbox (Channel, NextTryAtUtc) WHERE Status = 'Pending';
        CREATE INDEX IF NOT EXISTS IX_MsgHatchOutbox_Flags ON MsgHatchOutbox (Flags);
        CREATE TABLE IF NOT EXISTS MsgHatchChannelDown (
            Channel TEXT PRIMARY KEY, Reason TEXT, SinceUtc TEXT, NextCheckAtUtc TEXT, ErrorCode INTEGER, ErrorName TEXT,
            Note TEXT);
        """;

    private static readonly ConcurrentDictionary<string, bool> _created = new(StringComparer.Ordinal);

    /// <summary>Opens a connection from the pool of <c>Microsoft.Data.Sqlite</c>, and creates the schema at the first one.</summary>
    public static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            CreateOnce(connection, connectionString);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void CreateOnce(SqliteConnection connection, string connectionString)
    {
        if (_created.ContainsKey(connectionString))
        {
            return;
        }

        using var command = connection.Command(_create);
        command.ExecuteNonQuery();
        _created.TryAdd(connectionString, true);
    }
}
