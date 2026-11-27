using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;

/// <summary>The setup method of the PostgreSQL dispatch signal (section 4.7, D334).</summary>
public static class PostgreSqlDispatchSignalExtensions
{
    /// <summary>
    /// Selects <see cref="PostgreSqlDispatchSignal"/> on the connection string of the PostgreSQL store contexts, with or
    /// without the inbox in the transaction of the host (D334). Each host of the system calls it on the same database, also
    /// on another machine. The startup validation fails when the EF Core stores are not on PostgreSQL, and when
    /// <see cref="DispatchOptions.EnablePolling"/> is false for the inbox or the outbox, because a notification while the
    /// signal does not listen reaches a loop only through its poll (D190).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UsePostgreSqlDispatchSignal(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<PostgreSqlDispatchSignalSetupCheck>()
            .Validate<IServiceProvider>(
                static (_, services) => StoresAreOnPostgreSql(services),
                "UsePostgreSqlDispatchSignal needs the EF Core stores on PostgreSQL, for example with UseEntityFrameworkPostgreSqlStores, because the signal listens on their database.")
            .ValidateOnStart();
        RequirePolling(builder.Services, "Inbox");
        RequirePolling(builder.Services, "Outbox");
        return builder.UseDispatchSignal(
            static services => new PostgreSqlDispatchSignal(
                ConnectionStringOfTheStores(services),
                services.GetRequiredService<TimeProvider>(),
                services.GetService<ILogger<PostgreSqlDispatchSignal>>() ?? NullLogger<PostgreSqlDispatchSignal>.Instance),
            nameof(UsePostgreSqlDispatchSignal));
    }

    private static bool StoresAreOnPostgreSql(IServiceProvider services)
    {
        if (services.GetService<IDbContextFactory<MsgHatchDbContext>>() is not { } contexts)
        {
            return false;
        }

        using var context = contexts.CreateDbContext();
        return context.Database.IsNpgsql();
    }

    private static string ConnectionStringOfTheStores(IServiceProvider services)
    {
        using var context = services.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContext();
        return context.Database.GetConnectionString()!;
    }

    private static void RequirePolling(IServiceCollection services, string dispatcher) =>
        services.AddOptions<DispatchOptions>(dispatcher)
            .Validate(
                static options => options.EnablePolling,
                $"UsePostgreSqlDispatchSignal needs EnablePolling true in the dispatch options {dispatcher}, because a notification while the signal does not listen reaches a loop only through its poll (D190).")
            .ValidateOnStart();
}
