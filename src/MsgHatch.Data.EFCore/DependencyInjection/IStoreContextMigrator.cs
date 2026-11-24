using Microsoft.EntityFrameworkCore;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>The migrations of one registered MsgHatch context (D320).</summary>
internal interface IStoreContextMigrator
{
    /// <summary>Applies the migrations of the context, with the setup of the stores and an optional provider setup of the migration.</summary>
    Task MigrateAsync(Action<DbContextOptionsBuilder>? configure, CancellationToken ct);

    /// <summary>Gives an idempotent SQL script of the migrations of the context.</summary>
    string Script();
}
