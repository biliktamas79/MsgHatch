using Microsoft.EntityFrameworkCore.Infrastructure;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;

/// <summary>The setup method of the EF Core subscription store on SQL Server (section 4.7, D60, D318, D320).</summary>
public static class EntityFrameworkSqlServerSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects the EF Core subscription store on SQL Server in one call: <c>UseEntityFrameworkSubscriptionStore</c> with
    /// <c>UseSqlServer</c>, the dialect of <c>MsgHatch.Data.EFCore.SqlServer</c>, and the migrations of
    /// <see cref="MsgHatchSubscriptionDbContext"/> in this package. <c>MigrateMsgHatchAsync</c> applies them, with the
    /// history table <c>__SubscriptionMigrations</c>. The store shares <c>EntityFrameworkStoreOptions</c> with the
    /// EF Core stores (D59).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the SQL Server database.</param>
    /// <param name="configureSqlServer">An optional change of the SQL Server options, for example a retry on a transient failure.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkSqlServerSubscriptionStore(
        this MsgHatchBuilder builder, string connectionString, Action<SqlServerDbContextOptionsBuilder>? configureSqlServer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkSubscriptionStore(o => EntityFrameworkSqlServerStoreExtensions.UseSqlServer(
            o, connectionString, typeof(EntityFrameworkSqlServerSubscriptionStoreExtensions).Assembly, configureSqlServer));
    }
}
