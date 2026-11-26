using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Data.EFCore.MySql.DependencyInjection;

/// <summary>The setup method of the EF Core stores on MySQL (section 4.7, D60, D318, D320, D325).</summary>
public static class EntityFrameworkMySqlStoreExtensions
{
    /// <summary>
    /// The server version of the provider, MySQL 8.4. The setup sets it, because the detection of the provider opens a
    /// connection, and the setup and the startup validation open none.
    /// </summary>
    private static readonly ServerVersion _serverVersion = ServerVersion.Create(8, 4, 0, ServerType.MySql);

    /// <summary>
    /// Selects the EF Core stores on MySQL in one call: <c>UseEntityFrameworkStores</c> with <c>UseMySql</c> of
    /// <c>Microting.EntityFrameworkCore.MySql</c>, the dialect <see cref="MySqlStoreDialect"/>, and the migrations of
    /// <see cref="MsgHatchDbContext"/> in this package. <c>MigrateMsgHatchAsync</c> applies them. The tables have the prefix
    /// <c>MsgHatch</c> in the database of the connection, and the option <c>Schema</c> keeps its default (D326). Each time is
    /// a <c>datetime(6)</c>, which keeps microseconds (D319). The options bind from the section
    /// <c>EntityFramework</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:EntityFramework</c>, and a
    /// value from code wins. The public API has no type of the provider (D325).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the MySQL database.</param>
    /// <param name="configureOptions">An optional change of the options of the stores in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkMySqlStores(
        this MsgHatchBuilder builder, string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkStores(o => UseMySql(o, connectionString, typeof(MySqlStoreDialect).Assembly), configureOptions);
    }

    /// <summary>
    /// Selects the EF Core stores on MySQL as <see cref="UseEntityFrameworkMySqlStores(MsgHatchBuilder, string, Action{EntityFrameworkStoreOptions}?)"/>
    /// does, with the inbox in the transaction of the context <typeparamref name="TContext"/> of the host (D331, D332). The
    /// host registers the context on the same database, for example with <c>AddDbContext</c>, and maps the inbox in its
    /// model with <c>modelBuilder.MapMsgHatchInbox(this)</c>. The context gets the dialect and the MsgHatch options of the
    /// store contexts, and the inbox table has the prefix <c>MsgHatch</c>. The migrations of the host never contain the
    /// inbox table, so <c>MigrateMsgHatchAsync</c> still creates it (D320).
    /// </summary>
    /// <typeparam name="TContext">The context of the host.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the MySQL database of the context of the host.</param>
    /// <param name="configureOptions">An optional change of the options of the stores in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkMySqlStores<TContext>(
        this MsgHatchBuilder builder, string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkStores<TContext>(o => UseMySql(o, connectionString, typeof(MySqlStoreDialect).Assembly), configureOptions);
    }

    /// <summary>
    /// The MySQL options of a MsgHatch context of a MySQL package: the connection, the server version, the migrations in the
    /// assembly of the package, the dialect, and the character set of <see cref="MySqlStoreModelCustomizer"/>.
    /// </summary>
    /// <param name="options">The options of a context.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="migrationsAssembly">The assembly of the migrations of the context.</param>
    internal static void UseMySql(DbContextOptionsBuilder options, string connectionString, Assembly migrationsAssembly)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(migrationsAssembly);
        options.UseMySql(connectionString, _serverVersion, mysql => mysql.MigrationsAssembly(migrationsAssembly))
            .UseEntityFrameworkStoreDialect(MySqlStoreDialect.Instance)
            .ReplaceService<IModelCustomizer, MySqlStoreModelCustomizer>();
    }
}
