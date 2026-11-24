using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The options of a MsgHatch context (D318, D320): the provider setup of the host, then an optional provider setup of a
/// migration, then the schema, the collation, a history table of its own in the schema, and the migrations of
/// <see cref="StoreMigrationsAssembly"/>. A history table that the host set stays.
/// </summary>
/// <typeparam name="TContext">The context.</typeparam>
/// <param name="configure">The provider setup of the host.</param>
/// <param name="historyTable">The name of the history table of the migrations of the context.</param>
internal sealed class StoreContextSetup<TContext>(Action<DbContextOptionsBuilder> configure, string historyTable)
    where TContext : DbContext
{
    public void Configure(DbContextOptionsBuilder builder, EntityFrameworkStoreOptions options, Action<DbContextOptionsBuilder>? migration = null)
    {
        configure(builder);
        migration?.Invoke(builder);
        StoreOptionsExtension.UseOptions(builder, options);
        UseHistoryTable(builder, options.Schema);
        builder.ReplaceService<IMigrationsAssembly, StoreMigrationsAssembly>();
    }

    private void UseHistoryTable(DbContextOptionsBuilder builder, string schema)
    {
        if (builder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault() is { MigrationsHistoryTableName: null } relational)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                relational.WithMigrationsHistoryTableName(historyTable).WithMigrationsHistoryTableSchema(schema));
        }
    }
}
