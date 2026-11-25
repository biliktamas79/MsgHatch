using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;

/// <summary>The setup method of the EF Core subscription store on MySQL (section 4.7, D60, D318, D320, D325).</summary>
public static class EntityFrameworkMySqlSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects the EF Core subscription store on MySQL in one call: <c>UseEntityFrameworkSubscriptionStore</c> with
    /// <c>UseMySql</c>, the dialect of <c>MsgHatch.Data.EFCore.MySql</c>, and the migrations of
    /// <see cref="MsgHatchSubscriptionDbContext"/> in this package. <c>MigrateMsgHatchAsync</c> applies them, with the
    /// history table <c>__MsgHatchSubscriptionMigrations</c>. The table is <c>MsgHatchSubscription</c> in the database of the
    /// connection (D326). The store shares <c>EntityFrameworkStoreOptions</c> with the EF Core stores (D59).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the MySQL database.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkMySqlSubscriptionStore(this MsgHatchBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkSubscriptionStore(o => EntityFrameworkMySqlStoreExtensions.UseMySql(
            o, connectionString, typeof(EntityFrameworkMySqlSubscriptionStoreExtensions).Assembly));
    }
}
