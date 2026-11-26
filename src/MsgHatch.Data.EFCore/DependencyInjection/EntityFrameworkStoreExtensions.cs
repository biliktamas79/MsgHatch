using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>The setup method of the EF Core stores (section 4.7, D60).</summary>
public static class EntityFrameworkStoreExtensions
{
    /// <summary>
    /// Selects the EF Core stores on a relational EF Core provider (D120, D318): a pooled
    /// <see cref="IDbContextFactory{TContext}"/> of <see cref="MsgHatchDbContext"/> with the options of
    /// <paramref name="configure"/>, for example <c>o =&gt; o.UseSqlite(connectionString)</c>, then
    /// <see cref="EntityFrameworkInboxStore"/> and
    /// <see cref="EntityFrameworkOutboxStore"/>, each with its two parts and their logging wrappers, and
    /// <see cref="EntityFrameworkRegistrySettingsStore"/> with its logging wrapper (D130). Only
    /// <see cref="EntityFrameworkMigrationExtensions.MigrateMsgHatchAsync"/> changes the schema, with the migrations of a
    /// provider package or of the host, and the history table <c>__StoreMigrations</c>, or <c>__MsgHatchStoreMigrations</c> on
    /// a database with no schemas (D320, D326). The options bind from the
    /// section <c>EntityFramework</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:EntityFramework</c>,
    /// and a value from code wins. The startup validation fails for a provider that is not relational (D175).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">The options of the contexts, with the provider and its connection.</param>
    /// <param name="configureOptions">An optional change of the options of the stores in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEntityFrameworkStores(
        this MsgHatchBuilder builder, Action<DbContextOptionsBuilder> configure, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        AddOptions(builder, nameof(UseEntityFrameworkStores), configureOptions);
        AddContexts<MsgHatchDbContext>(builder.Services, nameof(UseEntityFrameworkStores), configure, "StoreMigrations");
        builder.Services.TryAddSingleton(static services =>
            new EntityFrameworkInboxStore(ContextsOf(services), services.GetRequiredService<MsgHatchJson>(), ClassifiersOf(services)));
        builder.Services.TryAddSingleton(static services =>
            new EntityFrameworkOutboxStore(ContextsOf(services), services.GetRequiredService<MsgHatchJson>(), ClassifiersOf(services)));
        builder.Services.TryAddSingleton(static services => new EntityFrameworkRegistrySettingsStore(ContextsOf(services), ClassifiersOf(services)));
        return builder
            .UseRegistrySettingsStore(static services => services.GetRequiredService<EntityFrameworkRegistrySettingsStore>(), nameof(UseEntityFrameworkStores))
            .UseInboxStore(static (services, key) => services.GetRequiredService<EntityFrameworkInboxStore>().For(key), nameof(UseEntityFrameworkStores))
            .UseInboxDispatchStore(static services => services.GetRequiredService<EntityFrameworkInboxStore>(), nameof(UseEntityFrameworkStores))
            .UseOutboxStore(static (services, key) => services.GetRequiredService<EntityFrameworkOutboxStore>().For(key), nameof(UseEntityFrameworkStores))
            .UseOutboxDispatchStore(static services => services.GetRequiredService<EntityFrameworkOutboxStore>(), nameof(UseEntityFrameworkStores));
    }

    /// <summary>
    /// Registers the shared <see cref="EntityFrameworkStoreOptions"/> of a setup method of the EF Core stores (D59): the
    /// binding of the section <c>EntityFramework</c> under the root of <c>AddMsgHatch</c>, by default
    /// <c>MsgHatch:EntityFramework</c>, the validation, and a value in code through
    /// <c>ConfigureSharedOptions</c> (D150).
    /// </summary>
    internal static void AddOptions(MsgHatchBuilder builder, string setupMethod, Action<EntityFrameworkStoreOptions>? configure)
    {
        builder.Services.AddOptions<EntityFrameworkStoreOptions>().BindConfiguration(builder.SectionPathOf(EntityFrameworkStoreOptions.SectionKey)).ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EntityFrameworkStoreOptions>, EntityFrameworkStoreOptionsValidator>());
        if (configure is not null)
        {
            builder.ConfigureSharedOptions(setupMethod, configure);
        }
    }

    /// <summary>
    /// Registers a pooled factory of a context, with the options of the host and of <see cref="EntityFrameworkStoreOptions"/>,
    /// the migrations of the context with a history table of its own (D320), and the startup check of its provider, which
    /// names the setup method (D175, D182). <paramref name="historyTable"/> has no <c>__</c> and no prefix, for example
    /// <c>StoreMigrations</c>.
    /// </summary>
    internal static void AddContexts<TContext>(
        IServiceCollection services, string setupMethod, Action<DbContextOptionsBuilder> configure, string historyTable)
        where TContext : DbContext
    {
        services.AddSingleton(new StoreContextSetup<TContext>(configure, historyTable));
        services.AddPooledDbContextFactory<TContext>(static (provider, options) => provider.GetRequiredService<StoreContextSetup<TContext>>()
            .Configure(options, provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStoreContextMigrator, StoreContextMigrator<TContext>>());
        services.AddOptions<EntityFrameworkSetupCheck<TContext>>().Configure(check => check.SetupMethod = setupMethod).ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EntityFrameworkSetupCheck<TContext>>, EntityFrameworkSetupValidator<TContext>>());
    }

    private static IDbContextFactory<MsgHatchDbContext> ContextsOf(IServiceProvider services) =>
        services.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();

    private static IEnumerable<IExceptionClassifier<MsgHatchDbContext>> ClassifiersOf(IServiceProvider services) =>
        services.GetServices<IExceptionClassifier<MsgHatchDbContext>>();
}
