using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>The migrations of <typeparamref name="TContext"/>, on a context of its own with the setup of the stores (D320).</summary>
/// <typeparam name="TContext">The context.</typeparam>
/// <param name="services">The services, which make the context.</param>
/// <param name="setup">The options of the context.</param>
/// <param name="options">The options of the stores.</param>
internal sealed class StoreContextMigrator<TContext>(
    IServiceProvider services, StoreContextSetup<TContext> setup, IOptions<EntityFrameworkStoreOptions> options) : IStoreContextMigrator
    where TContext : DbContext
{
    public async Task MigrateAsync(Action<DbContextOptionsBuilder>? configure, CancellationToken ct)
    {
        await using var context = Create(configure);
        await context.Database.MigrateAsync(ct);
    }

    public string Script()
    {
        using var context = Create(configure: null);
        return context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
    }

    private TContext Create(Action<DbContextOptionsBuilder>? configure)
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        setup.Configure(builder, options.Value, configure);
        return ActivatorUtilities.CreateInstance<TContext>(services, builder.Options);
    }
}
