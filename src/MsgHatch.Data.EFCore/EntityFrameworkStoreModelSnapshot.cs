using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The base class of the model snapshot of a MsgHatch context in a provider package (D320). It takes the schema and the
/// collation of the options at run time, as <see cref="EntityFrameworkStoreMigration"/> does. So a context with another
/// schema or collation has no pending model change against the migrations of the package.
/// </summary>
public abstract class EntityFrameworkStoreModelSnapshot : ModelSnapshot
{
    /// <summary>The schema of <see cref="EntityFrameworkStoreOptions.Schema"/>.</summary>
    protected string Schema { get; private set; } = EntityFrameworkStoreOptions.DefaultSchema;

    /// <summary>The collation of the text columns of the options or of the dialect, or null for the collation of the database.</summary>
    protected string? TextCollation { get; private set; }

    internal void UseOptions(StoreOptionsExtension options) => (Schema, TextCollation) = (options.Schema, options.TextCollation);
}
