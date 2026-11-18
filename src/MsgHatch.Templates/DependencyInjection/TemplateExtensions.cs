using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.DependencyInjection;

/// <summary>
/// The setup methods of the template library (D212, section 4.7): the template stores, and the template engine of each
/// payload type. A channel package calls <see cref="AddTemplateEngine{TEvent, TPayload}"/> for each registration that renders
/// templates.
/// </summary>
public static class TemplateExtensions
{
    /// <summary>Selects the shared template store, with its logging wrapper (D123).</summary>
    /// <typeparam name="TStore">The store type, a thread-safe singleton.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this MsgHatchBuilder builder)
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
    public static MsgHatchBuilder UseTenantTemplateStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this MsgHatchBuilder builder)
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
        return AddManager(builder);
    }

    /// <summary>
    /// Registers the template engine of one payload type for one registration of a channel, as a tenant service with
    /// its logging wrapper (D126, D213), with the parts that the channel reads (D220). The registrations of one payload
    /// type must give the same engine factory, and with the role <see cref="MsgHatchRoles.Consumer"/> each registration
    /// needs a template store (D62). The template commands check a written text of a declared part with this engine (D219).
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The builder of the payload type, which gives the message type.</param>
    /// <param name="registration">The name of the registration for the errors of the startup validation, for example <c>Email:OrderShipped</c>.</param>
    /// <param name="engineName">
    /// The name of the engine, for example <see cref="ITemplateEngineFactory.Name"/> or <c>PlaceholderTemplates.EngineName</c>,
    /// which <c>ITemplateQueries.GetTemplatePartsAsync</c> gives for each part (D240).
    /// </param>
    /// <param name="parts">The parts that the channel reads, at least one.</param>
    /// <param name="engine">The factory of the engine of one tenant key.</param>
    /// <returns>The builder of the payload type.</returns>
    /// <exception cref="ArgumentException">The parts are empty, or a part name is not a name of ASCII letters, digits, and <c>_</c> that starts with a letter.</exception>
    public static PayloadBuilder<TEvent, TPayload> AddTemplateEngine<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string registration, string engineName, IReadOnlyList<TemplatePart> parts,
        Func<IServiceProvider, TenantKey, ITemplateEngine<TPayload>> engine)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineName);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(engine);
        CheckParts(parts);
        TemplateSetup.Of(payload.Services).EngineSelections.Add(
            new EngineSelection(
                typeof(TPayload), registration, engineName, engine, payload.Type, [.. parts], CheckWithTheEngineOfTheKey<TPayload>,
                RenderWithTheEngineOfTheKey<TPayload>));
        AddManager(payload.Parent).AddTenantService(ServiceLifetime.Scoped, engine, TemplateLoggers.Wrap);
        return payload;
    }

    /// <summary>
    /// Gives the template registrations of the payload type and the message type a sample payload, for the preview of a
    /// template in an admin UI (D254, D256). For example
    /// <c>.WithPayload&lt;PasswordResetEmail, PasswordResetEmailLoader&gt;(p =&gt; p.AsEmail("FEmail", LiquidTemplates.Engine).WithTemplateSample(new PasswordResetEmail("https://example.com/reset")))</c>.
    /// A second sample of one payload type and one message type gives a setup error (D62).
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The builder of the payload type, which gives the message type.</param>
    /// <param name="sample">The sample payload. Use no real data, because each operator of the admin UI sees it.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> WithTemplateSample<TEvent, TPayload>(this PayloadBuilder<TEvent, TPayload> payload, TPayload sample)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(sample);
        TemplateSetup.Of(payload.Services).Samples.Add(new TemplateSample(payload.Type, typeof(TPayload), sample));
        return payload;
    }

    /// <summary>
    /// Selects the implementation of <see cref="ITemplateQueries"/>, for example an HTTP client, in place of the in-process
    /// default of the template setup methods, as a scoped tenant service with its logging wrapper (D126, D231, D244). The
    /// order of the calls does not matter. A second selection gives a setup error.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="factory">The factory of the queries of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="setupMethod">The setup method of a package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateQueries(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, ITemplateQueries> factory, string? setupMethod = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseTenantService(ServiceLifetime.Scoped, factory, TemplateLoggers.Wrap, setupMethod ?? nameof(UseTemplateQueries));
    }

    /// <summary>
    /// Selects the implementation of <see cref="ITemplateCommands"/>, for example an HTTP client, in place of the in-process
    /// default of the template setup methods, as a scoped tenant service with its logging wrapper (D126, D231, D244). The
    /// order of the calls does not matter. A second selection gives a setup error.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="factory">The factory of the commands of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="setupMethod">The setup method of a package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateCommands(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, ITemplateCommands> factory, string? setupMethod = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseTenantService(ServiceLifetime.Scoped, factory, TemplateLoggers.Wrap, setupMethod ?? nameof(UseTemplateCommands));
    }

    private static void CheckParts(IReadOnlyList<TemplatePart> parts)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("A registration of a template engine declares at least one part.", nameof(parts));
        }

        foreach (var part in parts)
        {
            if (!TemplatePartNames.IsValid(part.Name))
            {
                throw new ArgumentException($"The part name {part.Name} is not a name of ASCII letters, digits, and _ that starts with a letter.", nameof(parts));
            }
        }
    }

    private static ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckWithTheEngineOfTheKey<TPayload>(
        IServiceProvider tenantServices, TemplateLookup lookup, string text, CancellationToken ct) =>
        tenantServices.GetRequiredService<ITemplateEngine<TPayload>>().CheckAsync(lookup, text, ct);

    private static ValueTask<ErrorOr<string>> RenderWithTheEngineOfTheKey<TPayload>(
        IServiceProvider tenantServices, TemplateLookup lookup, string text, object sample, CancellationToken ct) =>
        tenantServices.GetRequiredService<ITemplateEngine<TPayload>>().RenderAsync(lookup, text, (TPayload)sample, ct);

    private static MsgHatchBuilder AddManager(MsgHatchBuilder builder) =>
        builder
            .AddDefaultTenantService<ITemplateQueries>(ServiceLifetime.Scoped, TemplateManager.For, TemplateLoggers.Wrap)
            .AddDefaultTenantService<ITemplateCommands>(ServiceLifetime.Scoped, TemplateManager.For, TemplateLoggers.Wrap);

    private static MsgHatchBuilder AddSharedStore(MsgHatchBuilder builder, Func<IServiceProvider, ITemplateStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        TemplateSetup.Of(builder.Services).SharedStoreSelections.Add(setupMethod);
        builder.Services.AddSingleton(services => TemplateLoggers.Wrap(services, TenantKey.None, factory(services)));
        return AddManager(builder);
    }

    private static TenantKey TenantKeyOf(object? key) =>
        key as TenantKey? ?? throw new InvalidOperationException("A tenant template store resolves only with a TenantKey as its key.");
}
