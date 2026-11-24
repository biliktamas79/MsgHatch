using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// Gives the EF Core tools the generator of <see cref="StoreMigrationsCodeGenerator"/>. A startup project of the tools
/// references it with <c>DesignTimeServicesReference</c>.
/// </summary>
public sealed class StoreMigrationsDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection serviceCollection) =>
        serviceCollection.AddSingleton<IMigrationsCodeGenerator, StoreMigrationsCodeGenerator>();
}
