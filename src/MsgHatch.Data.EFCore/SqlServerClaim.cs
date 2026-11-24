using Microsoft.EntityFrameworkCore;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The claim of D120 on SQL Server: one <c>UPDATE</c> with an <c>OUTPUT</c> clause of the rows of a <c>TOP</c> subquery
/// with <c>ROWLOCK, READPAST, UPDLOCK</c> in the claim order (D144). An <c>UPDATE</c> of a CTE with the <c>TOP</c> puts a
/// sort for the Halloween protection under the <c>TOP</c>, so it locks each due row, and a parallel claim gets fewer rows
/// (checked fact of phase 18).
/// </summary>
internal static class SqlServerClaim
{
    private const string _providerName = "Microsoft.EntityFrameworkCore.SqlServer";

    public static bool IsFor(DbContext context) => context.Database.ProviderName == _providerName;

    public static string Statement(ClaimStatementNames names) => $"""
        UPDATE {names.Table} SET {names.ClaimedDueAtUtc} = {names.NextTryAtUtc}, {names.ClaimToken} = {names.ClaimTokenParameter},
            {names.NextTryAtUtc} = {names.LeaseUntilUtcParameter}, {names.LeaseUntilUtc} = {names.LeaseUntilUtcParameter}
        OUTPUT inserted.*
        WHERE {names.Sequence} IN (
            SELECT TOP ({names.MaxCountParameter}) due.{names.Sequence} FROM {names.Table} AS due WITH (ROWLOCK, READPAST, UPDLOCK)
            WHERE due.{names.Status} = {names.PendingParameter}{ChannelCondition(names)} AND due.{names.NextTryAtUtc} <= {names.NowUtcParameter}
            ORDER BY due.{names.NextTryAtUtc}, due.{names.Sequence})
        """;

    private static string ChannelCondition(ClaimStatementNames names) =>
        names.Channel is null ? string.Empty : $" AND due.{names.Channel} = {names.ChannelParameter}";
}
