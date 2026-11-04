using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions.DependencyInjection;

/// <summary>The setup methods of the subscription router package (section 4.7, D97).</summary>
public static class SubscriptionExtensions
{
    /// <summary>
    /// Selects the subscription router as the router of each message type with no router of its own (D99), and
    /// registers <see cref="ResolutionTrace"/> as the trace type <c>Trace:Subscription</c> (D98). A message type with the
    /// subscription router needs a scope function from <see cref="WithScope"/>. With the role
    /// <see cref="MsgHatchRoles.Consumer"/>, the host needs a subscription store and a recipient directory.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptions(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        SubscriptionSetup.Of(builder.Services).UsesSubscriptions = true;
        return builder
            .UseRouter(typeof(SubscriptionRouter<>))
            .AddTraceType("Subscription", 1, SubscriptionJson.Default.ResolutionTrace);
    }

    /// <summary>Registers the scope function of a message type, which the subscription router needs (D16).</summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <param name="message">The builder of the message type.</param>
    /// <param name="scope">
    /// The scope of an event, for example <c>e =&gt; NotificationScope.ForUsers(e.CustomerIds)</c>. It must not throw.
    /// </param>
    /// <returns>The builder of the message type.</returns>
    public static MessageBuilder<TEvent> WithScope<TEvent>(this MessageBuilder<TEvent> message, Func<TEvent, NotificationScope> scope)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(scope);
        message.Services.AddSingleton(new SubscriptionMessage<TEvent>(scope));
        return message;
    }

    /// <summary>
    /// Registers a subscription store as a singleton tenant service, with its logging wrapper (D116, D126). A host calls
    /// it only for its own store.
    /// </summary>
    /// <typeparam name="TStore">The store type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionStore<TStore>(this MsgHatchBuilder builder)
        where TStore : class, ISubscriptionStore =>
        builder.UseSubscriptionStore(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TStore>(tenantServices));

    /// <summary>
    /// Registers a subscription store with a factory, for example for a test substitute. The store packages call it.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="factory">The factory of the store of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <param name="setupMethod">The setup method of a store package that calls this method, so that a second selection names it (D62).</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionStore(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, ISubscriptionStore> factory, string? setupMethod = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        SubscriptionSetup.Of(builder.Services).StoreSelections.Add(setupMethod ?? nameof(UseSubscriptionStore));
        return builder.AddTenantService(ServiceLifetime.Singleton, factory, SubscriptionLoggers.Wrap);
    }

    /// <summary>Registers the consumer's recipient directory as a scoped tenant service, with its logging wrapper (D76, D126).</summary>
    /// <typeparam name="TDirectory">The directory type. A constructor parameter of type <see cref="TenantKey"/> gets the key.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRecipientDirectory<TDirectory>(this MsgHatchBuilder builder)
        where TDirectory : class, IRecipientDirectory =>
        builder.UseRecipientDirectory(static (tenantServices, _) => ActivatorUtilities.CreateInstance<TDirectory>(tenantServices));

    /// <summary>Registers the recipient directory with a factory, for example for a test substitute.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="factory">The factory of the directory of one tenant key. Its service provider gives the tenant services of that key.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRecipientDirectory(
        this MsgHatchBuilder builder, Func<IServiceProvider, TenantKey, IRecipientDirectory> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        SubscriptionSetup.Of(builder.Services).DirectorySelections.Add(nameof(UseRecipientDirectory));
        return builder.AddTenantService(ServiceLifetime.Scoped, factory, SubscriptionLoggers.Wrap);
    }
}
