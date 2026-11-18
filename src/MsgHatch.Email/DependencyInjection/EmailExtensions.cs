using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Placeholders;

namespace MsgHatch.Email.DependencyInjection;

/// <summary>
/// The setup methods of the email package (section 4.7, D60, D61, D215). <c>AsEmail</c> extends the payload builder of
/// <c>WithPayload</c> (D136), so that C# infers the event type and the payload type of a call. The template stores come
/// from the setup methods of <c>MsgHatch.Templates</c> (D212).
/// </summary>
public static class EmailExtensions
{
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
        return Register(payload, name, PlaceholderTemplates.EngineName, PlaceholderTemplates.Engine<TPayload>, configure);
    }

    /// <summary>
    /// Registers the message type on the email channel for one payload type, with another template engine (D44, D216),
    /// for example the Liquid engine, or a test substitute. The email registrations of one payload type must select
    /// the same factory object (D62).
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="engine">The factory of the engine of one tenant key.</param>
    /// <param name="configure">The configuration of the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsEmail<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, ITemplateEngineFactory engine,
        Action<ChannelBuilder<EmailMessage>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return Register(payload, name, engine.Name, engine.Create<TPayload>, configure);
    }

    private static MsgHatchBuilder AddEmailChannel(MsgHatchBuilder builder) =>
        builder.AddChannelType(EmailChannel.Descriptor, 1, EmailJson.Default.EmailChannelSettings, new EmailChannelSettingsValidator());

    private static PayloadBuilder<TEvent, TPayload> Register<TEvent, TPayload>(
        PayloadBuilder<TEvent, TPayload> payload, string name, string engineName, Func<IServiceProvider, TenantKey, ITemplateEngine<TPayload>> engine,
        Action<ChannelBuilder<EmailMessage>>? configure)
    {
        ArgumentNullException.ThrowIfNull(payload);
        AddEmailChannel(payload.Parent);
        return payload.AddTemplateEngine($"{EmailChannel.Id.Name}:{name}", engineName, EmailChannel.TemplateParts, engine).AsChannel<EmailMessage, EmailComposer<TEvent, TPayload>>(
            EmailChannel.Id, name, 1, EmailJson.Default.EmailMessage, configure: configure);
    }
}
