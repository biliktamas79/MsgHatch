using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;

/// <summary>The services of both PostgreSql setup methods with the role Consumer, which needs no transport, and no database.</summary>
public static class MigratedSubscriptionStores
{
    /// <summary>New services of both PostgreSql setup methods with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkPostgreSqlStores(connectionString, configureOptions)
            .UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString);
        return services.Build();
    }
}
