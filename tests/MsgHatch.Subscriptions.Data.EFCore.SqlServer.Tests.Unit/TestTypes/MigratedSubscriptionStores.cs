using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Unit.TestTypes;

/// <summary>The services of both SqlServer setup methods with the role Consumer, which needs no transport, and no database.</summary>
public static class MigratedSubscriptionStores
{
    /// <summary>New services of both SqlServer setup methods with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkSqlServerStores(connectionString, configureOptions)
            .UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
        return services.Build();
    }
}
