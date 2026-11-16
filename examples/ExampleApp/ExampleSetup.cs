using ExampleApp.Notices;
using ExampleApp.PasswordResets;
using ExampleApp.Recipients;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.Subscriptions;
using MsgHatch.Subscriptions.Caching.FusionCache.DependencyInjection;
using MsgHatch.Subscriptions.Data.Sqlite.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ExampleApp;

/// <summary>
/// The registration code that each process of the example calls (section 4.9): the event consumer, a publisher, or one
/// process with both roles. A process adds its transports after it.
/// </summary>
public static class ExampleSetup
{
    /// <summary>The prefix of the embedded email templates.</summary>
    public const string TemplatePrefix = "ExampleApp.Templates";

    /// <summary>
    /// Registers MsgHatch with the subscription router, the example directory, the SQLite stores and the subscription
    /// store on one database file, the stored registry on the same file (section 4.20), the subscription cache on
    /// FusionCache, the embedded templates, and the message types A to F. The connection string comes from
    /// <c>MsgHatch:Sqlite:ConnectionString</c>.
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <param name="configure">Changes the options, for example the roles of the process.</param>
    /// <returns>The builder, for the transports of the process.</returns>
    public static MsgHatchBuilder AddExampleMsgHatch(this IServiceCollection services, Action<MsgHatchOptions>? configure = null) =>
        services.AddMsgHatch(configure)
            .UseSubscriptions()
            .UseRecipientDirectory<ExampleRecipientDirectory>()
            .UseSqliteStores()
            .UseStoredRegistry()
            .UseSqliteSubscriptionStore()
            .UseFusionSubscriptionCache()
            .UseEmbeddedTemplates(typeof(ExampleSetup).Assembly, TemplatePrefix)
            .AddExampleMessages();

    /// <summary>
    /// Registers the message types A to E of the subscription example, each with the test case <c>basic</c> for the routing
    /// verification, and the password reset F.
    /// </summary>
    /// <param name="builder">The MsgHatch builder.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder AddExampleMessages(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var json = ExampleJson.Default;
        return builder
            .AddMessage("A", 1, json.EventA, static m => m
                .WithScope(ScopeOf)
                .WithTestEvent("basic", static user => new EventA(TestUserOf(user), null, "A test of the routing."))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventA>>(static p => p.AsEmail("AEmail")))
            .AddMessage("B", 1, json.EventB, static m => m
                .WithScope(ScopeOf)
                .WithTestEvent("basic", static user => new EventB(TestUserOf(user), null, "A test of the routing."))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventB>>(static p => p.AsEmail("BEmail")))
            .AddMessage("C", 1, json.EventC, m => m
                .WithScope(ScopeOf)
                .WithTestEvent("basic", static user => new EventC(TestUserOf(user), null, "A test of the routing."))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventC>>(static p => p.AsEmail("CEmail"))
                .WithPayload<Notice, NoticeLoader<EventC>>(p => p.AsSignalR("CUpdate", 1, json.SignalREnvelopeNotice)))
            .AddMessage("D", 1, json.EventD, m => m
                .WithScope(ScopeOf)
                .WithTestEvent("basic", static user => new EventD(TestUserOf(user), null, "A test of the routing."))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventD>>(static p => p.AsEmail("DEmail"))
                .WithPayload<Notice, NoticeLoader<EventD>>(p => p
                    .AsSignalR("DUpdate", 1, json.SignalREnvelopeNotice)
                    .AsBroker("DEvent", 1, json.BrokerEnvelopeNotice)))
            .AddMessage("E", 1, json.EventE, m => m
                .WithScope(ScopeOf)
                .WithTestEvent("basic", static user => new EventE(TestUserOf(user), null, "A test of the routing."))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventE>>(static p => p.AsEmail("EEmail"))
                .WithPayload<Notice, NoticeLoader<EventE>>(p => p.AsBroker("EEvent", 1, json.BrokerEnvelopeNotice)))
            .AddMessage("F", 1, json.PasswordReset, static m => m
                .WithRouter<PasswordResetRouter>()
                .WithPayload<PasswordResetEmail, PasswordResetEmailLoader>(static p => p.AsEmail("FEmail")));
    }

    /// <summary>Turns on the traces and the metrics of MsgHatch (D131). The host adds its exporter after it.</summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The OpenTelemetry builder, for the exporter.</returns>
    public static OpenTelemetryBuilder AddExampleTelemetry(this IServiceCollection services) =>
        services.AddOpenTelemetry()
            .WithTracing(static tracing => tracing.AddSource(MsgHatchTelemetry.SourceName))
            .WithMetrics(static metrics => metrics.AddMeter(MsgHatchTelemetry.MeterName));

    private static NotificationScope ScopeOf(IExampleEvent exampleEvent) =>
        exampleEvent.CompanyId is { } company
            ? NotificationScope.ForCompany(CompanyId.From(company))
            : NotificationScope.ForUsers(UsersOf(exampleEvent));

    private static int[] TestUserOf(UserId user) => user.TryAs<int>(out var id) ? [id] : [];

    private static UserId[] UsersOf(IExampleEvent exampleEvent) => [.. (exampleEvent.UserIds ?? []).Select(static user => UserId.From(user))];
}
