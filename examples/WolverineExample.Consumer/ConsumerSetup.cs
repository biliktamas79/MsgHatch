using System.Text.Json.Serialization.Metadata;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.Subscriptions;
using MsgHatch.Subscriptions.Data.Sqlite.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using WolverineExample.Consumer.Notifications;
using WolverineExample.Consumer.Recipients;
using WolverineExample.Contracts;
using static System.FormattableString;

namespace WolverineExample.Consumer;

/// <summary>
/// The MsgHatch registration of the consumer: the subscription router, the SQLite directory, the SQLite stores and the
/// SQLite subscription store on one database file, the embedded email templates, and the five message types. The host
/// adds its transports after it.
/// </summary>
public static class ConsumerSetup
{
    /// <summary>The prefix of the embedded email templates.</summary>
    public const string TemplatePrefix = "WolverineExample.Consumer.Templates";

    /// <summary>
    /// Registers MsgHatch for the consumer, and the <see cref="NotificationHistory"/> of the hub. The connection string of
    /// the database file comes from <c>MsgHatch:Sqlite:ConnectionString</c>.
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <param name="configure">Changes the options, for example the roles of the process.</param>
    /// <returns>The builder, for the transports of the host.</returns>
    public static MsgHatchBuilder AddConsumerMsgHatch(this IServiceCollection services, Action<MsgHatchOptions>? configure = null)
    {
        services.AddSingleton<NotificationHistory>();
        return services.AddMsgHatch(configure)
            .UseSubscriptions()
            .UseRecipientDirectory<ConsumerDirectory>()
            .UseSqliteStores()
            .UseSqliteSubscriptionStore()
            .UseEmbeddedTemplates(typeof(ConsumerSetup).Assembly, TemplatePrefix)
            .AddConsumerMessages();
    }

    /// <summary>
    /// Registers the five message types, each on the email and the SignalR channel with one <see cref="Notification"/>.
    /// The scope of an event tells the subscription router the user or the company that the event concerns. The message
    /// type <c>TenantMaintenance</c> gets one <see cref="CompanyMaintenance"/> for each company of the tenant.
    /// </summary>
    /// <param name="builder">The MsgHatch builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder AddConsumerMessages(this MsgHatchBuilder builder)
    {
        var json = ConsumerJson.Default;
        return builder
            .AddNotification(
                nameof(UserLogin), json.UserLogin, static e => UserScope(e.UserId),
                static e => Invariant($"User {e.UserId} signed in at {e.LoggedInAtUtc:u}."))
            .AddNotification(
                nameof(CompanyArchived), json.CompanyArchived, static e => CompanyScope(e.CompanyId),
                static e => Invariant($"Company {e.CompanyId} was archived at {e.ArchivedAtUtc:u}."))
            .AddNotification(
                nameof(ProjectCreated), json.ProjectCreated, static e => UserScope(e.OwnerUserId),
                static e => Invariant($"Project {e.ProjectName} ({e.ProjectId}) of user {e.OwnerUserId} was created."))
            .AddNotification(
                nameof(ProjectFinished), json.ProjectFinished, static e => UserScope(e.OwnerUserId),
                static e => Invariant($"Project {e.ProjectName} ({e.ProjectId}) of user {e.OwnerUserId} is finished."))
            .AddNotification(
                nameof(TenantMaintenance), json.CompanyMaintenance, static e => CompanyScope(e.CompanyId),
                static e => Invariant($"The maintenance of {e.Maintenance.TenantId} runs from {e.Maintenance.StartsAtUtc:u} to {e.Maintenance.EndsAtUtc:u}."));
    }

    private static MsgHatchBuilder AddNotification<TEvent>(
        this MsgHatchBuilder builder, string type, JsonTypeInfo<TEvent> json, Func<TEvent, NotificationScope> scope, Func<TEvent, string> text) =>
        builder.AddMessage(type, 1, json, message => message
            .WithScope(scope)
            .WithPayload((_, _) => new NotificationLoader<TEvent>(text), payload => payload
                .AsEmail(type)
                .AsSignalR(type, 1, ConsumerJson.Default.SignalREnvelopeNotification)));

    private static NotificationScope UserScope(int user)
    {
        UserId[] users = [UserId.From(user)];
        return NotificationScope.ForUsers(users);
    }

    private static NotificationScope CompanyScope(int company) => NotificationScope.ForCompany(CompanyId.From(company));
}
