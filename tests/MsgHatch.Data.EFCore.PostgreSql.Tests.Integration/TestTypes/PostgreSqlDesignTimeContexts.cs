using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(PostgreSqlFixture))]
[assembly: DesignTimeServicesReference(
    "MsgHatch.Data.EFCore.Tests.Integration.TestTypes.StoreMigrationsDesignTimeServices, MsgHatch.Data.EFCore.Tests.Integration")]

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;

/// <summary>
/// The design-time contexts of the EF Core tools for the migrations of <c>MsgHatch.Data.EFCore.PostgreSql</c> (D320), from
/// <c>UseEntityFrameworkPostgreSqlStores</c>, with the generator of <see cref="StoreMigrationsCodeGenerator"/>. A new
/// migration: <c>dotnet ef migrations add Name --context MsgHatchDbContext --project src/MsgHatch.Data.EFCore.PostgreSql
/// --startup-project tests/MsgHatch.Data.EFCore.PostgreSql.Tests.Integration --output-dir Migrations</c>. The tools open
/// no connection.
/// </summary>
public sealed class PostgreSqlDesignTimeContexts : IDesignTimeDbContextFactory<MsgHatchDbContext>
{
    public MsgHatchDbContext CreateDbContext(string[] args)
    {
        var services = TestHost.CreateServices(configuration: StoreMigrationsCodeGenerator.DesignTimeOptions);
        services.AddMsgHatch().UseEntityFrameworkPostgreSqlStores("Host=localhost;Database=msghatch-design-time");
        return services.Build().GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContext();
    }
}
