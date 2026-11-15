using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Placeholders;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Razor.DependencyInjection;

/// <summary>The setup methods of the Razor engine (section 4.7, D196, D198).</summary>
public static class RazorEmailExtensions
{
    /// <summary>
    /// Selects the Razor email templates of an assembly (D198): the classes that the Razor SDK compiles from the files
    /// <c>{root}/{MessageType}.{Language}.body.razor</c> and <c>{root}/{CompanyId}/{MessageType}.{Language}.body.razor</c>.
    /// A second call fails the startup validation (D62).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="assembly">The assembly with the templates.</param>
    /// <param name="rootNamespace">The namespace of the folder <c>{root}</c>, for example <c>ExampleApp.EmailTemplates</c>.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRazorEmailTemplates(this MsgHatchBuilder builder, Assembly assembly, string rootNamespace)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNamespace);
        RazorEmailSetup.Of(builder.Services).TemplateSelections.Add($"{nameof(UseRazorEmailTemplates)}({rootNamespace})");
        builder.Services.AddSingleton(new RazorTemplateClasses(assembly, rootNamespace));
        return builder;
    }

    /// <summary>
    /// Registers the message type on the email channel for one payload type, with the Razor engine (D193): the html of
    /// a Razor template, and the subject and the text of the template store. All Razor registrations of one payload type
    /// share one engine.
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type, with the placeholder writer of the source generator.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="configure">The configuration of the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsRazorEmail<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, Action<ChannelBuilder<EmailMessage>>? configure = null)
        where TPayload : IPlaceholderPayload<TPayload>
    {
        ArgumentNullException.ThrowIfNull(payload);
        RazorEmailSetup.Of(payload.Services).Registrations.Add($"{EmailChannel.Id.Name}:{name}");
        return payload.AsEmail(name, RazorEngine<TPayload>, configure);
    }

    private static RazorEmailEngine<TPayload> RazorEngine<TPayload>(IServiceProvider tenantServices, TenantKey key)
        where TPayload : IPlaceholderPayload<TPayload>
    {
        var services = tenantServices.GetRequiredService<IServiceProvider>();
        return new RazorEmailEngine<TPayload>(
            services.GetRequiredService<RazorTemplateClasses>(),
            key == TenantKey.None ? null : services.GetKeyedService<ITemplateStore>(key),
            services.GetService<ITemplateStore>(),
            services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.DefaultLanguage,
            tenantServices,
            services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance);
    }
}
