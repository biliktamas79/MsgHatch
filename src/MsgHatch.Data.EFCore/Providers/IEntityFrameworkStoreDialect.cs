using System.Data.Common;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore.Providers;

/// <summary>
/// The contract of a provider package of the EF Core stores (D318), for example <c>MsgHatch.Data.EFCore.SqlServer</c>.
/// The base package runs on each relational EF Core provider with no dialect. A dialect adds what one provider does
/// better: the default collation of the ids and the names, and a claim statement. A provider package gives it to the
/// contexts with <see cref="EntityFrameworkStoreDialectExtensions.UseEntityFrameworkStoreDialect"/>.
/// </summary>
public interface IEntityFrameworkStoreDialect
{
    /// <summary>
    /// The name of the EF Core provider that the dialect serves, for example
    /// <c>Microsoft.EntityFrameworkCore.SqlServer</c>. The startup validation fails when the provider of a context has
    /// another name.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// The default collation of each text column, so of each id and name, for example <c>Latin1_General_100_BIN2</c>, or
    /// null for the collation of the database. <see cref="EntityFrameworkStoreOptions.TextCollation"/> wins over it.
    /// </summary>
    string? TextCollation { get; }

    /// <summary>
    /// The type of each JSON column, for example <c>json</c>, or null for the text type of the provider (D323).
    /// <see cref="EntityFrameworkStoreOptions.JsonColumnType"/> wins over it. The default gives null, so a dialect of a host
    /// still compiles.
    /// </summary>
    string? JsonColumnType => null;

    /// <summary>
    /// Whether the database has schemas inside it (D326). With false, for example on MySQL, where a schema is a database,
    /// each table and each history table gets the prefix <c>MsgHatch</c> and no schema, for example <c>MsgHatchOutbox</c>
    /// and <c>__MsgHatchStoreMigrations</c>. Then <see cref="EntityFrameworkStoreOptions.Schema"/> has no effect, so the
    /// startup validation fails when it is not the default. The default gives true, so a dialect of a host still compiles.
    /// </summary>
    bool HasSchemas => true;

    /// <summary>
    /// Gives the claim statement of one message table, or null for the claim of the base package: a LINQ read of the
    /// due candidates and one <c>ExecuteUpdate</c> with a claim token. The base package runs the statement with
    /// <c>FromSqlRaw</c> and the parameters of <paramref name="names"/>.
    /// </summary>
    /// <param name="names">The quoted names of the table, the columns, and the parameters of the statement.</param>
    /// <returns>The SQL text, or null.</returns>
    string? ClaimStatement(ClaimStatementNames names);

    /// <summary>
    /// Gives the error type of a database exception of the provider, or null for the built-in mapping of the stores
    /// (D322). The stores ask the classifiers of the host first, and give the dialect the <see cref="DbException"/> also
    /// when EF Core wraps it, for example in a <c>DbUpdateException</c>. A non-transient type stops the retries of the
    /// message. The default gives null, so a dialect of a host still compiles.
    /// </summary>
    /// <param name="exception">The database exception.</param>
    /// <returns>The error type, or null.</returns>
    ErrorType? ErrorTypeOf(DbException exception) => null;
}
