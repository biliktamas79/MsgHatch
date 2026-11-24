using System.Data.Common;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.Failures;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql;

/// <summary>
/// The dialect of the EF Core stores on PostgreSQL (D318). The default collation is <c>C</c>, so an id or a name is
/// case-sensitive and sorts in ordinal order. The claim is one <c>UPDATE ... FROM</c> a subquery that reads the due rows
/// in the claim order (D144), stops after the maximum count with <c>LIMIT</c>, and locks them with
/// <c>FOR UPDATE SKIP LOCKED</c>: a parallel claim skips the rows that another claim locked, and takes the next due rows.
/// The subquery gives the due time before the claim, and <c>RETURNING</c> gives each claimed row with its values after the
/// claim. A missing permission or a failed login is rejected, and a missing table or column or a value that is too long
/// fails the validation, so the dispatcher does not retry them (D322).
/// </summary>
public sealed class PostgreSqlStoreDialect : IEntityFrameworkStoreDialect
{
    private PostgreSqlStoreDialect()
    {
    }

    /// <summary>The one instance of the dialect.</summary>
    public static PostgreSqlStoreDialect Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <inheritdoc />
    public string? TextCollation => "C";

    /// <inheritdoc />
    public string? ClaimStatement(ClaimStatementNames names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return $"""
            UPDATE {names.Table} AS claimed SET {names.ClaimedDueAtUtc} = due.{names.NextTryAtUtc}, {names.ClaimToken} = {names.ClaimTokenParameter},
                {names.NextTryAtUtc} = {names.LeaseUntilUtcParameter}, {names.LeaseUntilUtc} = {names.LeaseUntilUtcParameter}
            FROM (
                SELECT {names.Sequence}, {names.NextTryAtUtc} FROM {names.Table}
                WHERE {names.Status} = {names.PendingParameter}{ChannelCondition(names)} AND {names.NextTryAtUtc} <= {names.NowUtcParameter}
                ORDER BY {names.NextTryAtUtc}, {names.Sequence}
                LIMIT {names.MaxCountParameter}
                FOR UPDATE SKIP LOCKED) AS due
            WHERE claimed.{names.Sequence} = due.{names.Sequence}
            RETURNING claimed.*
            """;
    }

    /// <inheritdoc />
    public ErrorType? ErrorTypeOf(DbException exception) => (exception as PostgresException)?.SqlState switch
    {
        PostgresErrorCodes.InsufficientPrivilege or PostgresErrorCodes.InvalidAuthorizationSpecification or PostgresErrorCodes.InvalidPassword =>
            Errors.Rejected,
        PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.StringDataRightTruncation =>
            Errors.ValidationFailed,
        _ => null,
    };

    private static string ChannelCondition(ClaimStatementNames names) =>
        names.Channel is null ? string.Empty : $" AND {names.Channel} = {names.ChannelParameter}";
}
