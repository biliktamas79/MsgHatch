using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;

/// <summary>The setup method of the EF Core stores on PostgreSQL (section 4.7, D60, D318, D320).</summary>
public static class EntityFrameworkPostgreSqlStoreExtensions
{
    /// <summary>
    /// Selects the EF Core stores on PostgreSQL in one call: <c>UseEntityFrameworkStores</c> with <c>UseNpgsql</c>, the
    /// dialect <see cref="PostgreSqlStoreDialect"/>, and the migrations of <see cref="MsgHatchDbContext"/> in this package.
    /// <c>MigrateMsgHatchAsync</c> applies them. Each time is a <c>timestamptz</c>, which keeps microseconds (D319). The
    /// options bind from the section <c>MsgHatch:EntityFramework</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="connectionString">The connection string of the PostgreSQL database.</param>
    /// <param name="configureOptions">An optional change of the options of the stores in code.</param>
    /// <param name="configureNpgsql">An optional change of the Npgsql options, for example a retry on a transient failure.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkPostgreSqlStores(
        this MsgHatchBuilder builder, string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return builder.UseEntityFrameworkStores(o => UseNpgsql(o, connectionString, configureNpgsql), configureOptions);
    }

    /// <summary>
    /// The Npgsql options of a MsgHatch context of a PostgreSQL package: the connection, the migrations in the assembly of
    /// the package, and the dialect.
    /// </summary>
    /// <param name="options">The options of a context.</param>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="migrationsAssembly">The assembly of the migrations of the context.</param>
    /// <param name="configureNpgsql">An optional change of the Npgsql options.</param>
    internal static void UseNpgsql(
        DbContextOptionsBuilder options, string connectionString, System.Reflection.Assembly migrationsAssembly,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(migrationsAssembly);
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(migrationsAssembly);
            configureNpgsql?.Invoke(npgsql);
        }).UseEntityFrameworkStoreDialect(PostgreSqlStoreDialect.Instance);
    }

    private static void UseNpgsql(DbContextOptionsBuilder options, string connectionString, Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql) =>
        UseNpgsql(options, connectionString, typeof(PostgreSqlStoreDialect).Assembly, configureNpgsql);
}
