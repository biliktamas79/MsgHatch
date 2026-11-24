using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// Plays a host with no provider package of MsgHatch (D320): it keeps the migrations of <see cref="MsgHatchDbContext"/>
/// on SQLite in its own assembly, in the folder <c>SqliteMigrations</c>. The EF Core tools generate them with this
/// factory: <c>dotnet ef migrations add Initial --context MsgHatchDbContext --project tests/MsgHatch.Data.EFCore.Tests.Unit
/// --output-dir SqliteMigrations</c>.
/// </summary>
public sealed class SqliteHostMigrations : IDesignTimeDbContextFactory<MsgHatchDbContext>
{
    /// <summary>The SQLite setup of the host, with this assembly as the migrations assembly.</summary>
    public static void UseSqlite(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlite(connectionString, static sqlite => sqlite.MigrationsAssembly(typeof(SqliteHostMigrations).Assembly.GetName().Name));

    public MsgHatchDbContext CreateDbContext(string[] args)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkStores(static o => UseSqlite(o, "Data Source=msghatch-design-time.db"));
        return services.Build().GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContext();
    }
}
