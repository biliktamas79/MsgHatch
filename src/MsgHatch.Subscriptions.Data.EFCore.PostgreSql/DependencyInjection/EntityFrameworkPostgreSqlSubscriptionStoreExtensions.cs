using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;

/// <summary>The setup method of the EF Core subscription store on PostgreSQL (section 4.7, D60, D318, D320).</summary>
public static class EntityFrameworkPostgreSqlSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects the EF Core subscription store on PostgreSQL in one call: <c>UseEntityFrameworkSubscriptionStore</c> with
    /// <c>UseNpgsql</c>, the dialect of <c>MsgHatch.Data.EFCore.PostgreSql</c>, and the migrations of
    /// <see cref="MsgHatchSubscriptionDbContext"/> in this package. <c>MigrateMsgHatchAsync</c> applies them, with the
    /// history table <c>__MsgHatchSubscriptionMigrations</c>. The store shares <c>EntityFrameworkStoreOptions</c> with the
    /// EF Core stores (D59).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the PostgreSQL database.</param>
    /// <param name="configureNpgsql">An optional change of the Npgsql options, for example a retry on a transient failure.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkPostgreSqlSubscriptionStore(
        this MsgHatchBuilder builder, string connectionString, Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkSubscriptionStore(o => EntityFrameworkPostgreSqlStoreExtensions.UseNpgsql(
            o, connectionString, typeof(EntityFrameworkPostgreSqlSubscriptionStoreExtensions).Assembly, configureNpgsql));
    }
}
