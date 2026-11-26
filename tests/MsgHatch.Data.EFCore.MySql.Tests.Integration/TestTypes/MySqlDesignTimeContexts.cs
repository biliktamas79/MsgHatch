using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

[assembly: AssemblyFixture(typeof(MySqlFixture))]
[assembly: DesignTimeServicesReference(
    "MsgHatch.Data.EFCore.Tests.Integration.TestTypes.StoreMigrationsDesignTimeServices, MsgHatch.Data.EFCore.Tests.Integration")]

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;

/// <summary>
/// The design-time contexts of the EF Core tools for the migrations of <c>MsgHatch.Data.EFCore.MySql</c> (D320), from
/// <c>UseEntityFrameworkMySqlStores</c>, with the generator of <see cref="StoreMigrationsCodeGenerator"/>. A new migration:
/// <c>dotnet ef migrations add Name --context MsgHatchDbContext --project src/MsgHatch.Data.EFCore.MySql --startup-project
/// tests/MsgHatch.Data.EFCore.MySql.Tests.Integration --output-dir Migrations</c>. The tools open no connection, because the
/// setup sets the server version.
/// </summary>
public sealed class MySqlDesignTimeContexts : IDesignTimeDbContextFactory<MsgHatchDbContext>
{
    public MsgHatchDbContext CreateDbContext(string[] args)
    {
        var services = EntityFrameworkTestHost.CreateServices(configuration: StoreMigrationsCodeGenerator.DesignTimeOptions);
        services.AddMsgHatch().UseEntityFrameworkMySqlStores("Server=localhost;Database=msghatch-design-time");
        return services.Build().GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContext();
    }
}
