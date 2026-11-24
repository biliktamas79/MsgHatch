using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;

namespace SchemaMigrator;

/// <summary>
/// The schema migrator of a pipeline (D320): it applies the MsgHatch migrations of the stores and of the subscription
/// store on SQL Server or on PostgreSQL, or with <c>--script path</c> it writes their idempotent SQL script. Only the pipeline needs the
/// rights to change the schema. The service gets only the rights to read and write rows.
/// </summary>
public static class Migrator
{
    /// <summary>The exit code of a successful run.</summary>
    public const int Success = 0;

    /// <summary>The exit code of a run with no connection string, an unknown provider, or unknown arguments.</summary>
    public const int UsageError = 2;

    private const string _usage = """
        Usage: SchemaMigrator [--script <path>]
        Set the environment variable SchemaMigrator__ConnectionString to a connection string with the rights to change the schema.
        Set SchemaMigrator__Provider to SqlServer (the default) or PostgreSql.
        With no argument, the migrator applies the MsgHatch migrations. With --script, it writes their idempotent SQL script.
        """;

    /// <summary>Runs the migrator with the arguments of the command line.</summary>
    /// <param name="args">No argument, or <c>--script</c> and the path of the script.</param>
    /// <param name="output">The output of the messages.</param>
    /// <param name="ct">The cancel token.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        var builder = Host.CreateApplicationBuilder();
        var options = builder.Configuration.GetSection(SchemaMigratorOptions.SectionName).Get<SchemaMigratorOptions>() ?? new SchemaMigratorOptions();
        if (new SchemaMigratorOptionsValidator().Validate(name: null, options).Failed || args is not ([] or ["--script", _]))
        {
            await output.WriteLineAsync(_usage);
            return UsageError;
        }

        using var host = Build(builder, options.ConnectionString, options.Provider);
        if (args is ["--script", var path])
        {
            await File.WriteAllTextAsync(path, await host.Services.ScriptMsgHatchMigrationsAsync(ct), ct);
            await output.WriteLineAsync($"Wrote the idempotent script of the MsgHatch migrations to {path}.");
        }
        else
        {
            await host.Services.MigrateMsgHatchAsync(ct: ct);
            await output.WriteLineAsync("Applied the MsgHatch migrations.");
        }

        return Success;
    }

    /// <summary>
    /// The host of the migrator: MsgHatch with no role and no message type, the EF Core stores and the subscription store on
    /// SQL Server or on PostgreSQL with the migrations of their packages.
    /// </summary>
    /// <param name="builder">The builder of the host.</param>
    /// <param name="connectionString">The connection string with the rights to change the schema.</param>
    /// <param name="provider">The database, <see cref="SchemaMigratorOptions.SqlServer"/> or <see cref="SchemaMigratorOptions.PostgreSql"/>.</param>
    /// <returns>The host.</returns>
    public static IHost Build(HostApplicationBuilder builder, string connectionString, string provider = SchemaMigratorOptions.SqlServer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var msgHatch = builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None);
        if (provider == SchemaMigratorOptions.PostgreSql)
        {
            msgHatch.UseEntityFrameworkPostgreSqlStores(connectionString).UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString);
        }
        else
        {
            msgHatch.UseEntityFrameworkSqlServerStores(connectionString).UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
        }

        return builder.Build();
    }
}
