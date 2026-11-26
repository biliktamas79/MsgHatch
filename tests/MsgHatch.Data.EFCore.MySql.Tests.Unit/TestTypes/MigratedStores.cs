using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.MySql.Tests.Unit.TestTypes;

/// <summary>The stores of <c>MsgHatch.Data.EFCore.MySql</c> with its dialect, with no database.</summary>
public static class MigratedStores
{
    /// <summary>New services of <c>UseEntityFrameworkMySqlStores</c> with the role Consumer, which needs no transport.</summary>
    public static ServiceProvider Build(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions = null) =>
        Services(connectionString, configureOptions).Build();

    /// <summary>
    /// New services of <c>UseEntityFrameworkMySqlStores</c> with the role Consumer, and doubles in the options of each
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

    /// <summary>
    /// New services of <c>UseEntityFrameworkMySqlStores&lt;TestHostContext&gt;</c> with the role Consumer, and the context of the
    /// host with <c>AddDbContext</c> on its own connection string.
    /// </summary>
    public static ServiceProvider BuildWithHostContext(string connectionString, string hostConnectionString)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContext>(o => o.UseMySql(hostConnectionString, ServerVersion.Create(8, 4, 0, ServerType.MySql)));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkMySqlStores<TestHostContext>(connectionString);
        return services.Build();
    }

    private static IServiceCollection Services(string connectionString, Action<EntityFrameworkStoreOptions>? configureOptions)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkMySqlStores(connectionString, configureOptions);
        return services;
    }
}
