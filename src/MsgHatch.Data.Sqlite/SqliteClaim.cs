using System.Text;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.Sqlite;

/// <summary>The SQL statements of the claim of one message table.</summary>
/// <param name="NextDue">Reads the earliest <c>NextTryAtUtc</c> of the pending rows.</param>
/// <param name="Due">Reads the <c>Sequence</c> of the first due rows, in the claim order <c>NextTryAtUtc, Sequence</c> (D144).</param>
/// <param name="Lease">
/// Leases the rows of the JSON array <c>@sequences</c>, and returns the <c>Sequence</c> and the document of each leased row.
/// </param>
/// <param name="Fail">Fails the row of <c>@sequence</c> with the error <c>@lastError</c>.</param>
internal sealed record ClaimStatements(string NextDue, string Due, string Lease, string Fail);

/// <summary>
/// The claim of section 4.5 (D103). A read with no write lock finds the earliest pending row, so a claim with nothing
/// due takes no write lock. Else, in a <c>BEGIN IMMEDIATE</c> transaction, one statement reads the first due rows in the
/// claim order (D144), and a second statement leases them, so the claim is atomic between processes. A row that the code
/// cannot read stays out of the result: a newer version stays leased for a newer instance (section 4.13), and a
/// permanent error fails the row.
/// </summary>
internal static class SqliteClaim
{
    public static ClaimResult<T> Run<T>(
        SqliteConnection connection, ClaimStatements statements, string? channel, int maxCount, DateTime nowUtc,
        DateTime leaseUntilUtc, Func<byte[], ErrorOr<T>> read)
    {
        var now = SqliteValues.Time(nowUtc);
        var earliest = NextDue(connection, statements.NextDue, channel, transaction: null);
        if (earliest is null || string.CompareOrdinal(earliest, now) > 0)
        {
            return new ClaimResult<T>([], TimeOf(earliest));
        }

        using var transaction = connection.BeginTransaction(deferred: false);
        var due = FirstDue(connection, transaction, statements.Due, channel, maxCount, now);
        var documents = Lease(connection, transaction, statements.Lease, due, SqliteValues.Time(leaseUntilUtc));
        var claimed = new List<T>();
        foreach (var sequence in due)
        {
            var message = read(Encoding.UTF8.GetBytes(documents[sequence]));
            if (message.IsError)
            {
                SetAside(connection, transaction, statements.Fail, sequence, message.Error!);
                continue;
            }

            claimed.Add(message.Value);
        }

        var nextDueAtUtc = due.Count < maxCount ? TimeOf(NextDue(connection, statements.NextDue, channel, transaction)) : nowUtc;
        transaction.Commit();
        return new ClaimResult<T>(claimed, nextDueAtUtc);
    }

    private static string? NextDue(SqliteConnection connection, string sql, string? channel, SqliteTransaction? transaction)
    {
        using var command = connection.Command(sql, transaction).With("@channel", channel);
        return command.ExecuteScalar() as string;
    }

    private static List<long> FirstDue(
        SqliteConnection connection, SqliteTransaction transaction, string sql, string? channel, int maxCount, string now)
    {
        using var command = connection.Command(sql, transaction)
            .With("@channel", channel)
            .With("@maxCount", maxCount)
            .With("@nowUtc", now);
        using var reader = command.ExecuteReader();
        var due = new List<long>();
        while (reader.Read())
        {
            due.Add(reader.GetInt64(0));
        }

        return due;
    }

    /// <summary>Leases the rows, and gives the document of each leased row by its <c>Sequence</c>.</summary>
    private static Dictionary<long, string> Lease(
        SqliteConnection connection, SqliteTransaction transaction, string sql, List<long> sequences, string leaseUntil)
    {
        using var command = connection.Command(sql, transaction)
            .With("@sequences", $"[{string.Join(',', sequences)}]")
            .With("@leaseUntilUtc", leaseUntil);
        using var reader = command.ExecuteReader();

        // SQLite gives the rows of RETURNING in an arbitrary order, and RETURNING has no ORDER BY.
        var documents = new Dictionary<long, string>();
        while (reader.Read())
        {
            documents.Add(reader.GetInt64(0), reader.GetString(1));
        }

        return documents;
    }

    private static void SetAside(SqliteConnection connection, SqliteTransaction transaction, string sql, long sequence, Error error)
    {
        if (error.Type.IsTransient)
        {
            return;
        }

        using var command = connection.Command(sql, transaction)
            .With("@sequence", sequence)
            .With("@lastError", $"{error.Type.Name}: {error.Message}");
        command.ExecuteNonQuery();
    }

    private static DateTime? TimeOf(string? text) => text is null ? null : SqliteValues.ParseTime(text);
}
