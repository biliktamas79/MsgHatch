using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Unit.TestTypes;

/// <summary>The stores of <c>MsgHatch.Data.EFCore.SqlServer</c> with its dialect, with no database.</summary>
public static class MigratedStores
{
    /// <summary>New services of <c>UseEntityFrameworkSqlServerStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        Services(connectionString, configureOptions).Build();

    /// <summary>
    /// New services of <c>UseEntityFrameworkSqlServerStores</c> with the role Consumer, and doubles in the options of each
    /// context after the setup, for example a doubled connection or a doubled EF Core service. The pooled factory of the
    /// setup reads the options of its contexts once, so the change is a singleton.
    /// </summary>
    public static ServiceProvider BuildWithDoubles(
        string connectionString, Action<DbContextOptionsBuilder> doubles, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = Services(connectionString, configureOptions);
        services.ConfigureDbContext<MsgHatchDbContext>(doubles, ServiceLifetime.Singleton);
        return services.Build();
    }

    private static IServiceCollection Services(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkSqlServerStores(connectionString, configureOptions);
        return services;
    }
}
