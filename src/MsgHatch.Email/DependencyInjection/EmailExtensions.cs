using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Placeholders;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.DependencyInjection;

/// <summary>
/// The setup methods of the email package (section 4.7, D60, D61). <c>AsEmail</c> extends the payload builder of
/// <c>WithPayload</c> (D136), so that C# infers the event type and the payload type of a call.
/// </summary>
public static class EmailExtensions
{
    /// <summary>Selects the shared template store of the placeholder engine, with its logging wrapper (D123).</summary>
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
        EmailSetup.Of(builder.Services).TenantStoreSelections.Add(nameof(UseTenantTemplateStore));
        builder.Services.AddKeyedSingleton(
            KeyedService.AnyKey,
            (services, key) => EmailLoggers.Wrap(services, TenantKeyOf(key), factory(services, TenantKeyOf(key))));
        return builder;
    }

    /// <summary>
    /// Selects an email client, with its logging wrapper, and <see cref="EmailTransport"/> as the transport of the email
    /// channel, with its logging wrapper (D129). The host is then a publisher of the email channel (D101). It also
    /// registers the email channel, so that a publisher host with no message type binds the options of the channel.
    /// </summary>
    /// <typeparam name="TClient">The client type, a thread-safe singleton.</typeparam>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEmailClient<TClient>(this MsgHatchBuilder builder)
        where TClient : class, IEmailClient =>
        builder.UseEmailClient(static services => ActivatorUtilities.CreateInstance<TClient>(services));

    /// <summary>Selects an email client with a factory, for example for a test substitute, and the email transport.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="factory">The factory of the client.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseEmailClient(this MsgHatchBuilder builder, Func<IServiceProvider, IEmailClient> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.Services.AddSingleton(services => EmailLoggers.Wrap(services, factory(services)));
        return AddEmailChannel(builder).UseTransport(static services => new EmailTransport(services.GetRequiredService<IEmailClient>()));
    }

    /// <summary>
    /// Registers the message type on the email channel for one payload type, with
    /// <see cref="EmailComposer{TEvent, TPayload}"/> and the placeholder engine (section 4.7). The composer gets the
    /// payload loader of <c>WithPayload</c> (D136). The content type is <c>Email:{name}</c>. The envelope is the library
    /// type <see cref="EmailMessage"/>, so the registration needs no version and no JSON type information (D17).
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="configure">The configuration of the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsEmail<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, Action<ChannelBuilder<EmailMessage>>? configure = null)
        where TPayload : IPlaceholderPayload<TPayload>
    {
        ArgumentNullException.ThrowIfNull(payload);
        EmailSetup.Of(payload.Services).PlaceholderRegistrations.Add($"{EmailChannel.Id.Name}:{name}");
        return Register(payload, name, PlaceholderEngine<TPayload>, configure);
    }

    /// <summary>
    /// Registers the message type on the email channel for one payload type, with another template engine (D44), for
    /// example <c>static (services, _) =&gt; ActivatorUtilities.CreateInstance&lt;RazorEngine&gt;(services)</c>, or a test
    /// substitute.
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="engine">The factory of the engine of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="configure">The configuration of the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsEmail<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, Func<IServiceProvider, TenantKey, IEmailTemplateEngine<TPayload>> engine,
        Action<ChannelBuilder<EmailMessage>>? configure = null) =>
        Register(payload, name, engine, configure);

    private static MsgHatchBuilder AddEmailChannel(MsgHatchBuilder builder) =>
        builder.AddChannelType(EmailChannel.Descriptor, 1, EmailJson.Default.EmailChannelSettings, new EmailChannelSettingsValidator());

    private static MsgHatchBuilder AddSharedStore(MsgHatchBuilder builder, Func<IServiceProvider, ITemplateStore> factory, string setupMethod)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        EmailSetup.Of(builder.Services).SharedStoreSelections.Add(setupMethod);
        builder.Services.AddSingleton(services => EmailLoggers.Wrap(services, TenantKey.None, factory(services)));
        return builder;
    }


    private static PayloadBuilder<TEvent, TPayload> Register<TEvent, TPayload>(
        PayloadBuilder<TEvent, TPayload> payload, string name, Func<IServiceProvider, TenantKey, IEmailTemplateEngine<TPayload>> engine,
        Action<ChannelBuilder<EmailMessage>>? configure)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(engine);
        EmailSetup.Of(payload.Services).EngineSelections.Add((typeof(TPayload), $"{EmailChannel.Id.Name}:{name}", engine));
        AddEmailChannel(payload.Parent).AddTenantService(ServiceLifetime.Scoped, engine, EmailLoggers.Wrap);
        return payload.AsChannel<EmailMessage, EmailComposer<TEvent, TPayload>>(
            EmailChannel.Id, name, 1, EmailJson.Default.EmailMessage, configure: configure);
    }

    private static PlaceholderEmailEngine<TPayload> PlaceholderEngine<TPayload>(IServiceProvider tenantServices, TenantKey key)
        where TPayload : IPlaceholderPayload<TPayload>
    {
        var services = tenantServices.GetRequiredService<IServiceProvider>();
        return new PlaceholderEmailEngine<TPayload>(
            key == TenantKey.None ? null : services.GetKeyedService<ITemplateStore>(key),
            services.GetService<ITemplateStore>(),
            services.GetRequiredService<IOptions<MsgHatchOptions>>());
    }

    private static TenantKey TenantKeyOf(object? key) =>
        key as TenantKey? ?? throw new InvalidOperationException("A tenant template store resolves only with a TenantKey as its key.");
}
