using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]
[assembly: DesignTimeServicesReference(
    "MsgHatch.Data.EFCore.Tests.Integration.TestTypes.StoreMigrationsDesignTimeServices, MsgHatch.Data.EFCore.Tests.Integration")]

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration.TestTypes;

/// <summary>
/// The design-time contexts of the EF Core tools for the migrations of <c>MsgHatch.Subscriptions.Data.EFCore.SqlServer</c>
/// (D320), from <c>UseEntityFrameworkSqlServerSubscriptionStore</c>, with the generator of
/// <see cref="StoreMigrationsCodeGenerator"/>. A new migration: <c>dotnet ef migrations add Name --context
/// MsgHatchSubscriptionDbContext --project src/MsgHatch.Subscriptions.Data.EFCore.SqlServer --startup-project
/// tests/MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration --output-dir Migrations</c>. The tools open no
/// connection.
/// </summary>
public sealed class SqlServerDesignTimeContexts : IDesignTimeDbContextFactory<MsgHatchSubscriptionDbContext>
{
    public MsgHatchSubscriptionDbContext CreateDbContext(string[] args)
    {
        var services = TestHost.CreateServices(configuration: StoreMigrationsCodeGenerator.DesignTimeOptions);
        services.AddMsgHatch().UseEntityFrameworkSqlServerSubscriptionStore("Server=localhost;Database=msghatch-design-time;Integrated Security=true");
        return services.Build().GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContext();
    }
}
