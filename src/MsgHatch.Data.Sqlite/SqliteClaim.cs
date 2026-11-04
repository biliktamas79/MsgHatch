using System.Text;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.Sqlite;

/// <summary>The SQL statements of the claim of one message table.</summary>
/// <param name="NextDue">Reads the earliest <c>NextTryAtUtc</c> of the pending rows.</param>
/// <param name="Lease">Leases the first due rows, and returns the <c>Sequence</c> and the document of each leased row.</param>
/// <param name="Fail">Fails the row of <c>@sequence</c> with the error <c>@lastError</c>.</param>
internal sealed record ClaimStatements(string NextDue, string Lease, string Fail);

/// <summary>
/// The claim of section 4.5 (D103). A read with no write lock finds the earliest pending row, so a claim with nothing
/// due takes no write lock. Else one statement in a <c>BEGIN IMMEDIATE</c> transaction leases the first due rows, so the
/// claim is atomic between processes. A row that the code cannot read stays out of the result: a newer version stays
/// leased for a newer instance (section 4.13), and a permanent error fails the row.
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
        var rows = LeaseDueRows(connection, transaction, statements.Lease, channel, maxCount, now, SqliteValues.Time(leaseUntilUtc));
        var claimed = new List<T>();
        foreach (var (sequence, document) in rows)
        {
            var message = read(Encoding.UTF8.GetBytes(document));
            if (message.IsError)
            {
                SetAside(connection, transaction, statements.Fail, sequence, message.Error!);
                continue;
            }

            claimed.Add(message.Value);
        }

        var nextDueAtUtc = rows.Count < maxCount ? TimeOf(NextDue(connection, statements.NextDue, channel, transaction)) : nowUtc;
        transaction.Commit();
        return new ClaimResult<T>(claimed, nextDueAtUtc);
    }

    private static string? NextDue(SqliteConnection connection, string sql, string? channel, SqliteTransaction? transaction)
    {
        using var command = connection.Command(sql, transaction).With("@channel", channel);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Gives the leased rows in the order of their insert.</summary>
    private static List<(long Sequence, string Document)> LeaseDueRows(
        SqliteConnection connection, SqliteTransaction transaction, string sql, string? channel, int maxCount, string now, string leaseUntil)
    {
        using var command = connection.Command(sql, transaction)
            .With("@channel", channel)
            .With("@maxCount", maxCount)
            .With("@nowUtc", now)
            .With("@leaseUntilUtc", leaseUntil);
        using var reader = command.ExecuteReader();
        var rows = new List<(long Sequence, string Document)>();
        while (reader.Read())
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        // SQLite gives the rows of RETURNING in an arbitrary order.
        rows.Sort(static (first, second) => first.Sequence.CompareTo(second.Sequence));
        return rows;
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
