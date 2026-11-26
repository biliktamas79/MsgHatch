using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// Fails each open of a connection before the connection opens. It throws the exception of the test, or by default the
/// <see cref="SqliteException"/> of a database file that SQLite cannot open, as a database that does not exist gives it.
/// It counts the opens.
/// </summary>
/// <param name="failure">The exception of each open, or null for the exception of a database that does not exist.</param>
public sealed class FailingConnectionInterceptor(Exception? failure = null) : DbConnectionInterceptor
{
    private const int _sqliteCantOpen = 14;

    private int _opens;

    public int Opens => _opens;

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        throw Failure();

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        throw Failure();

    private Exception Failure()
    {
        Interlocked.Increment(ref _opens);
        return failure ?? new SqliteException("unable to open database file", _sqliteCantOpen);
    }
}
