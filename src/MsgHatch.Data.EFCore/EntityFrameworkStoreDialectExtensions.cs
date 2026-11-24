using Microsoft.EntityFrameworkCore;

namespace MsgHatch.Data.EFCore;

/// <summary>The option of the contexts of the EF Core stores that a provider package sets (D318).</summary>
public static class EntityFrameworkStoreDialectExtensions
{
    /// <summary>
    /// Gives the contexts of the stores the dialect of a provider package: the default collation of the ids and the
    /// names, and an optional claim statement. A provider package calls it in its setup method, for example
    /// <c>o =&gt; o.UseSqlServer(connectionString).UseEntityFrameworkStoreDialect(dialect)</c>. The startup validation
    /// fails when the dialect serves another provider than the provider of the context.
    /// </summary>
    /// <param name="builder">The options of a context.</param>
    /// <param name="dialect">The dialect.</param>
    /// <returns>The options.</returns>
    public static DbContextOptionsBuilder UseEntityFrameworkStoreDialect(this DbContextOptionsBuilder builder, IEntityFrameworkStoreDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(dialect);
        StoreOptionsExtension.UseDialect(builder, dialect);
        return builder;
    }
}
