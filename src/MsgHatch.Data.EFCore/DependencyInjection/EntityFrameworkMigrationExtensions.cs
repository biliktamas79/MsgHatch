using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>The migration step of the EF Core stores (D320). No other code of MsgHatch changes the schema.</summary>
public static class EntityFrameworkMigrationExtensions
{
    /// <summary>
    /// Applies the migrations of each registered MsgHatch context: <see cref="MsgHatchDbContext"/> and the subscription
    /// context. A provider package ships the migrations of its context, and a host with no provider package generates
    /// them in its own assembly with <c>MigrationsAssembly</c>. A host calls the method at its start, or a separate
    /// console app of the pipeline calls it, so the service needs only the rights to read and write rows. EF Core locks
    /// the database during the migration, so two callers do not apply one migration two times. A failed migration
    /// throws the exception of EF Core. MsgHatch never logs a connection string.
    /// </summary>
    /// <param name="services">The services of a host with a setup method of the EF Core stores.</param>
    /// <param name="configure">
    /// An optional provider setup of the migration on top of the setup of the stores, for example
    /// <c>o =&gt; o.UseSqlServer(connectionStringWithSchemaRights)</c>. With no value, the migration uses the setup of
    /// the stores.
    /// </param>
    /// <param name="ct">The cancel token.</param>
    /// <returns>The task of the migration.</returns>
    public static async Task MigrateMsgHatchAsync(
        this IServiceProvider services, Action<DbContextOptionsBuilder>? configure = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var migrator in services.GetServices<IStoreContextMigrator>())
        {
            await migrator.MigrateAsync(configure, ct);
        }
    }

    /// <summary>
    /// Gives an idempotent SQL script of the migrations of each registered MsgHatch context, for a pipeline or a database
    /// administrator that runs only SQL. A second run of the script changes nothing.
    /// </summary>
    /// <param name="services">The services of a host with a setup method of the EF Core stores.</param>
    /// <param name="ct">The cancel token.</param>
    /// <returns>The script.</returns>
    public static Task<string> ScriptMsgHatchMigrationsAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(string.Join(Environment.NewLine, services.GetServices<IStoreContextMigrator>().Select(static migrator => migrator.Script())));
    }
}
