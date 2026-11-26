using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Data.EFCore.SqlServer.DependencyInjection;

/// <summary>The setup method of the EF Core stores on SQL Server (section 4.7, D60, D318, D320).</summary>
public static class EntityFrameworkSqlServerStoreExtensions
{
    /// <summary>
    /// Selects the EF Core stores on SQL Server in one call: <c>UseEntityFrameworkStores</c> with <c>UseSqlServer</c>, the
    /// dialect <see cref="SqlServerStoreDialect"/>, and the migrations of <see cref="MsgHatchDbContext"/> in this package.
    /// <c>MigrateMsgHatchAsync</c> applies them. The options bind from the section <c>EntityFramework</c> under the
    /// root of <c>AddMsgHatch</c>, by default <c>MsgHatch:EntityFramework</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the SQL Server database.</param>
    /// <param name="configureOptions">An optional change of the options of the stores in code.</param>
    /// <param name="configureSqlServer">An optional change of the SQL Server options, for example a retry on a transient failure.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkSqlServerStores(
        this MsgHatchBuilder builder, string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null,
        Action<SqlServerDbContextOptionsBuilder>? configureSqlServer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkStores(o => UseSqlServer(o, connectionString, configureSqlServer), configureOptions);
    }

    /// <summary>
    /// Selects the EF Core stores on SQL Server as <see cref="UseEntityFrameworkSqlServerStores(MsgHatchBuilder, string, Action{EntityFrameworkStoreOptions}?, Action{SqlServerDbContextOptionsBuilder}?)"/>
    /// does, with the inbox in the transaction of the context <typeparamref name="TContext"/> of the host (D331, D332). The
    /// host registers the context on the same database, for example with <c>AddDbContext</c>, and maps the inbox in its
    /// model with <c>modelBuilder.MapMsgHatchInbox(this)</c>. The context gets the dialect and the MsgHatch options of the
    /// store contexts. The migrations of the host never contain the inbox table, so <c>MigrateMsgHatchAsync</c> still
    /// creates it (D320).
    /// </summary>
    /// <typeparam name="TContext">The context of the host.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the SQL Server database of the context of the host.</param>
    /// <param name="configureOptions">An optional change of the options of the stores in code.</param>
    /// <param name="configureSqlServer">An optional change of the SQL Server options of the store contexts.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkSqlServerStores<TContext>(
        this MsgHatchBuilder builder, string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null,
        Action<SqlServerDbContextOptionsBuilder>? configureSqlServer = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkStores<TContext>(o => UseSqlServer(o, connectionString, configureSqlServer), configureOptions);
    }

    /// <summary>
    /// The SQL Server options of a MsgHatch context of a SQL Server package: the connection, the migrations in the
    /// assembly of the package, and the dialect.
    /// </summary>
    /// <param name="options">The options of a context.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="migrationsAssembly">The assembly of the migrations of the context.</param>
    /// <param name="configureSqlServer">An optional change of the SQL Server options.</param>
    internal static void UseSqlServer(
        DbContextOptionsBuilder options, string connectionString, System.Reflection.Assembly migrationsAssembly,
        Action<SqlServerDbContextOptionsBuilder>? configureSqlServer = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(migrationsAssembly);
        options.UseSqlServer(connectionString, sqlServer =>
        {
            sqlServer.MigrationsAssembly(migrationsAssembly);
            configureSqlServer?.Invoke(sqlServer);
        }).UseEntityFrameworkStoreDialect(SqlServerStoreDialect.Instance);
    }

    private static void UseSqlServer(DbContextOptionsBuilder options, string connectionString, Action<SqlServerDbContextOptionsBuilder>? configureSqlServer) =>
        UseSqlServer(options, connectionString, typeof(SqlServerStoreDialect).Assembly, configureSqlServer);
}
