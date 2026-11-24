using Microsoft.EntityFrameworkCore.Migrations;
using MsgHatch.Data.EFCore.Model;

namespace MsgHatch.Data.EFCore.Migrations;

/// <summary>
/// The base class of a migration of a MsgHatch context in a provider package (D320). The EF Core tools write the schema
/// and the collation of the design time into a migration. A provider package writes <see cref="Schema"/> and
/// <see cref="TextCollation"/> in their place, so each migration takes the schema and the collation of the options at
/// run time. A migration that a host generates in its own assembly needs no base class.
/// </summary>
public abstract class EntityFrameworkStoreMigration : Migration
{
    /// <summary>The schema of <see cref="EntityFrameworkStoreOptions.Schema"/>.</summary>
    protected string Schema { get; private set; } = EntityFrameworkStoreOptions.DefaultSchema;

    /// <summary>The collation of the text columns of the options or of the dialect, or null for the collation of the database.</summary>
    protected string? TextCollation { get; private set; }

    internal void UseOptions(StoreOptionsExtension options) => (Schema, TextCollation) = (options.Schema, options.TextCollation);
}
