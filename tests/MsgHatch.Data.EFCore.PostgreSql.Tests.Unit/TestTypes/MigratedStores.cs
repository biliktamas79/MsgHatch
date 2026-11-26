using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;

/// <summary>The stores of <c>MsgHatch.Data.EFCore.PostgreSql</c> with its dialect, with no database.</summary>
public static class MigratedStores
{
    /// <summary>New services of <c>UseEntityFrameworkPostgreSqlStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkPostgreSqlStores(connectionString, configureOptions);
        return services.Build();
    }
}
