using System.Data.Common;
using Microsoft.Data.SqlClient;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore.SqlServer;

/// <summary>
/// The dialect of the EF Core stores on SQL Server (D120, D318). The default collation is <c>Latin1_General_100_BIN2</c>,
/// so an id or a name is case-sensitive and sorts in ordinal order. The claim is one <c>UPDATE</c> with an
/// <c>OUTPUT</c> clause of the rows of a <c>TOP</c> subquery with <c>ROWLOCK, READPAST, UPDLOCK</c> in the claim order
/// (D144): a parallel claim skips the rows that another claim locked, and takes the next due rows. An <c>UPDATE</c> of a
/// CTE with the <c>TOP</c> puts a sort for the Halloween protection under the <c>TOP</c>, so it locks each due row, and a
/// parallel claim gets fewer rows (checked fact of phase 18). A missing permission or a failed login is rejected, and a
/// missing table or column or a value that is too long fails the validation, so the dispatcher does not retry them (D322).
/// </summary>
public sealed class SqlServerStoreDialect : IEntityFrameworkStoreDialect
{
    private const int _invalidColumnName = 207;
    private const int _invalidObjectName = 208;
    private const int _objectPermissionDenied = 229;
    private const int _columnPermissionDenied = 230;
    private const int _databasePermissionDenied = 262;
    private const int _valueTruncatedInColumn = 2628;
    private const int _valueTruncated = 8152;
    private const int _loginFailed = 18456;

    private SqlServerStoreDialect()
    {
    }

    /// <summary>The one instance of the dialect.</summary>
    public static SqlServerStoreDialect Instance { get; } = new();

    /// <inheritdoc />
    public string ProviderName => "Microsoft.EntityFrameworkCore.SqlServer";

    /// <inheritdoc />
    public string? TextCollation => "Latin1_General_100_BIN2";

    /// <inheritdoc />
    public string? ClaimStatement(ClaimStatementNames names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return $"""
            UPDATE {names.Table} SET {names.ClaimedDueAtUtc} = {names.NextTryAtUtc}, {names.ClaimToken} = {names.ClaimTokenParameter},
                {names.NextTryAtUtc} = {names.LeaseUntilUtcParameter}, {names.LeaseUntilUtc} = {names.LeaseUntilUtcParameter}
            OUTPUT inserted.*
            WHERE {names.Sequence} IN (
                SELECT TOP ({names.MaxCountParameter}) due.{names.Sequence} FROM {names.Table} AS due WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE due.{names.Status} = {names.PendingParameter}{ChannelCondition(names)} AND due.{names.NextTryAtUtc} <= {names.NowUtcParameter}
                ORDER BY due.{names.NextTryAtUtc}, due.{names.Sequence})
            """;
    }

    /// <inheritdoc />
    public ErrorType? ErrorTypeOf(DbException exception) => (exception as SqlException)?.Number switch
    {
        _objectPermissionDenied or _columnPermissionDenied or _databasePermissionDenied or _loginFailed => Errors.Rejected,
        _invalidColumnName or _invalidObjectName or _valueTruncatedInColumn or _valueTruncated => Errors.ValidationFailed,
        _ => null,
    };

    private static string ChannelCondition(ClaimStatementNames names) =>
        names.Channel is null ? string.Empty : $" AND due.{names.Channel} = {names.ChannelParameter}";
}
