using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Unit.TestTypes;

/// <summary>The services of both MySql setup methods with the role Consumer, which needs no transport, and no database.</summary>
public static class MigratedSubscriptionStores
{
    /// <summary>New services of both MySql setup methods with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkMySqlStores(connectionString, configureOptions)
            .UseEntityFrameworkMySqlSubscriptionStore(connectionString);
        return services.Build();
    }
}
