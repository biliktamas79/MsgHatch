using System.Data.Common;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// A dialect of the EF Core SQLite provider for the tests of the dialect contract (D318): a claim statement with
/// <c>UPDATE ... RETURNING</c>, which SQLite runs in one write, and an optional default collation. It counts its claim
/// statements, and gives one error type for each database exception. It can act as the dialect of a database with no
/// schemas.
/// </summary>
/// <param name="providerName">The provider that the dialect serves.</param>
/// <param name="textCollation">The default collation, or null.</param>
/// <param name="hasClaimStatement">Whether the dialect gives a claim statement.</param>
/// <param name="errorType">The error type of each database exception, or null for the built-in mapping.</param>
/// <param name="jsonColumnType">The type of the JSON columns, or null for the text type of the provider.</param>
/// <param name="hasSchemas">Whether the database of the dialect has schemas (D326).</param>
public sealed class SqliteTestDialect(
    string providerName = SqliteTestDialect.SqliteProvider, string? textCollation = null, bool hasClaimStatement = true, ErrorType? errorType = null,
    string? jsonColumnType = null, bool hasSchemas = true)
    : IEntityFrameworkStoreDialect
{
    public const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    private int _claimStatements;

    public string ProviderName { get; } = providerName;

    public string? TextCollation { get; } = textCollation;

    public string? JsonColumnType { get; } = jsonColumnType;

    public bool HasSchemas { get; } = hasSchemas;

    public int ClaimStatements => _claimStatements;

    public ErrorType? ErrorTypeOf(DbException exception) => errorType;

    public string? ClaimStatement(ClaimStatementNames names)
    {
        ArgumentNullException.ThrowIfNull(names);
        Interlocked.Increment(ref _claimStatements);
        return hasClaimStatement
            ? $"""
                UPDATE {names.Table} SET {names.ClaimedDueAtUtc} = {names.NextTryAtUtc}, {names.ClaimToken} = {names.ClaimTokenParameter},
                    {names.NextTryAtUtc} = {names.LeaseUntilUtcParameter}, {names.LeaseUntilUtc} = {names.LeaseUntilUtcParameter}
                WHERE {names.Sequence} IN (
                    SELECT {names.Sequence} FROM {names.Table}
                    WHERE {names.Status} = {names.PendingParameter}{(names.Channel is null ? "" : $" AND {names.Channel} = {names.ChannelParameter}")}
                        AND {names.NextTryAtUtc} <= {names.NowUtcParameter}
                    ORDER BY {names.NextTryAtUtc}, {names.Sequence} LIMIT {names.MaxCountParameter})
                RETURNING *
                """
            : null;
    }
}
