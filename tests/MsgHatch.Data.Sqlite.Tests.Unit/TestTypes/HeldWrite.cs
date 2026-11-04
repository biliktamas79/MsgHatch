using Microsoft.Data.Sqlite;

namespace MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>A write transaction of a first writer on its own connection. A dispose without a commit rolls it back.</summary>
public sealed class HeldWrite(SqliteConnection connection, SqliteTransaction transaction) : IDisposable
{
    public void Commit() => transaction.Commit();

    public void Dispose()
    {
        transaction.Dispose();
        connection.Dispose();
    }
}
