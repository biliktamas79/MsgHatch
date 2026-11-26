using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// The services and the context options of the EF Core tests. EF Core counts its internal service providers for the whole
/// process, and fails the 21st with <see cref="CoreEventId.ManyServiceProvidersCreatedWarning"/>. The tests make one on
/// purpose for each combination of the provider, the schema, the collation, the JSON type, and the dialect, so each test
/// context ignores that warning. Each EF Core test project links this file.
/// </summary>
internal static class EntityFrameworkTestHost
{
    /// <summary>The services of <see cref="TestHost.CreateServices"/>, in which each context ignores the warning.</summary>
    public static IServiceCollection CreateServices(TimeProvider? time = null, IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var services = TestHost.CreateServices(time, configuration);
        services.AddSingleton(typeof(IDbContextOptionsConfiguration<>), typeof(ManyServiceProvidersAllowed<>));
        return services;
    }

    public static void IgnoreManyServiceProvidersWarning(this DbContextOptionsBuilder options) =>
        options.ConfigureWarnings(static warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

    private sealed class ManyServiceProvidersAllowed<TContext> : IDbContextOptionsConfiguration<TContext>
        where TContext : DbContext
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.IgnoreManyServiceProvidersWarning();
    }
}
