using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.DependencyInjection;

/// <summary>
/// The setup methods of the template library (D212, section 4.7): the template stores, and the template engine of each
/// payload type. A channel package calls <see cref="AddTemplateEngine{TPayload}"/> for each registration that renders
/// templates.
/// </summary>
public static class TemplateExtensions
{
    /// <summary>Selects the shared template store, with its logging wrapper (D123).</summary>
    /// <typeparam name="TStore">The store type, a thread-safe singleton.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateStore<TStore>(this MsgHatchBuilder builder)
        where TStore : class, ITemplateStore =>
        builder.UseTemplateStore(static services => ActivatorUtilities.CreateInstance<TStore>(services));

    /// <summary>Selects the shared template store with a factory, for example for a test substitute.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="factory">The factory of the store.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateStore(this MsgHatchBuilder builder, Func<IServiceProvider, ITemplateStore> factory) =>
        AddSharedStore(builder, factory, nameof(UseTemplateStore));

    /// <summary>
    /// Selects the embedded resources of an assembly as the shared template store, with
    /// <see cref="EmbeddedResourceTemplateStore"/> (section 4.10).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="assembly">The assembly with the resources.</param>
    /// <param name="prefix">The prefix of the resource names, for example <c>ExampleApp.Templates</c>.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEmbeddedTemplates(this MsgHatchBuilder builder, Assembly assembly, string prefix)
    {
        var store = new EmbeddedResourceTemplateStore(assembly, prefix);
        return AddSharedStore(builder, _ => store, nameof(UseEmbeddedTemplates));
    }

    /// <summary>
    /// Selects a folder as the shared template store, with <see cref="FileSystemTemplateStore"/> (section 4.10). The
    /// options bind from the section <c>MsgHatch:FileTemplates</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFileTemplates(this MsgHatchBuilder builder, Action<FileTemplateOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<FileTemplateOptions>().BindConfiguration(FileTemplateOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FileTemplateOptions>, FileTemplateOptionsValidator>());
        return AddSharedStore(
            builder,
            static services => new FileSystemTemplateStore(services.GetRequiredService<IOptions<FileTemplateOptions>>().Value.RootPath),
            nameof(UseFileTemplates));
    }

    /// <summary>Selects a folder as the shared template store, with <see cref="FileSystemTemplateStore"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="rootPath">The folder of the template files.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFileTemplates(this MsgHatchBuilder builder, string rootPath) =>
        builder.UseFileTemplates(o => o.RootPath = rootPath);

    /// <summary>
    /// Selects the template store of each tenant, as a keyed <see cref="ITemplateStore"/> with no unkeyed registration,
    /// with its logging wrapper. The unkeyed store is the shared store, so the two never collide (D62, D123).
    /// </summary>
    /// <typeparam name="TStore">The store type, with a constructor parameter of type <see cref="TenantKey"/>.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTenantTemplateStore<TStore>(this MsgHatchBuilder builder)
        where TStore : class, ITemplateStore =>
        builder.UseTenantTemplateStore(static (services, key) => ActivatorUtilities.CreateInstance<TStore>(services, key));

    /// <summary>
    /// Selects the template store of each tenant with a factory, for example a <see cref="FileSystemTemplateStore"/> on
    /// the folder of the tenant.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="factory">The factory of the store of one tenant key.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTenantTemplateStore(this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, ITemplateStore> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        TemplateSetup.Of(builder.Services).TenantStoreSelections.Add(nameof(UseTenantTemplateStore));
        builder.Services.AddKeyedSingleton(
            KeyedService.AnyKey,
            (services, key) => TemplateLoggers.Wrap(services, TenantKeyOf(key), factory(services, TenantKeyOf(key))));
        return builder;
    }

    /// <summary>
    /// Registers the template engine of one payload type for one registration of a channel, as a tenant service with
    /// its logging wrapper (D126, D213). The registrations of one payload type must give the same engine factory, and
    /// with the role <see cref="MsgHatchRoles.Consumer"/> each registration needs a template store (D62).
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="registration">The name of the registration for the errors of the startup validation, for example <c>Email:OrderShipped</c>.</param>
    /// <param name="engine">The factory of the engine of one tenant key.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder AddTemplateEngine<TPayload>(
        this MsgHatchBuilder builder, string registration, Func<IServiceProvider, TenantKey, ITemplateEngine<TPayload>> engine)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration);
        ArgumentNullException.ThrowIfNull(engine);
        TemplateSetup.Of(builder.Services).EngineSelections.Add((typeof(TPayload), registration, engine));
        return builder.AddTenantService(ServiceLifetime.Scoped, engine, TemplateLoggers.Wrap);
    }

    private static MsgHatchBuilder AddSharedStore(MsgHatchBuilder builder, Func<IServiceProvider, ITemplateStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        TemplateSetup.Of(builder.Services).SharedStoreSelections.Add(setupMethod);
        builder.Services.AddSingleton(services => TemplateLoggers.Wrap(services, TenantKey.None, factory(services)));
        return builder;
    }

    private static TenantKey TenantKeyOf(object? key) =>
        key as TenantKey? ?? throw new InvalidOperationException("A tenant template store resolves only with a TenantKey as its key.");
}
