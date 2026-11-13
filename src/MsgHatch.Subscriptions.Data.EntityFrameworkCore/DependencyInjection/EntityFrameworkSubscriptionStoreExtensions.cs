using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EntityFrameworkCore;
using MsgHatch.Data.EntityFrameworkCore.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Data.EntityFrameworkCore.DependencyInjection;

/// <summary>The setup method of the EF Core subscription store (section 4.7, D60).</summary>
public static class EntityFrameworkSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects <see cref="EntityFrameworkSubscriptionStore"/>, with its logging wrapper, one instance for each tenant key,
    /// on a pooled <see cref="IDbContextFactory{TContext}"/> of <see cref="MsgHatchSubscriptionDbContext"/> with the
    /// options of <paramref name="configure"/>, for example <c>o =&gt; o.UseSqlServer(connectionString)</c>. It shares the
    /// <see cref="EntityFrameworkStoreOptions"/> of <c>UseEntityFrameworkStores</c> (D59): the options bind from the
    /// section <c>MsgHatch:EntityFramework</c>. The package creates no schema: the host creates it with its own EF Core
    /// migrations of <see cref="MsgHatchSubscriptionDbContext"/>.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">The options of the contexts, with the provider and its connection.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkSubscriptionStore(this MsgHatchBuilder builder, Action<DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        EntityFrameworkStoreExtensions.AddOptions(builder, nameof(UseEntityFrameworkSubscriptionStore), configure: null);
        EntityFrameworkStoreExtensions.AddContexts<MsgHatchSubscriptionDbContext>(builder.Services, configure);
        return builder.UseSubscriptionStore(
            static (services, key) => new EntityFrameworkSubscriptionStore(
                services.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>(), key, services.GetRequiredService<ChannelSettingsJson>()),
            nameof(UseEntityFrameworkSubscriptionStore));
    }
}
