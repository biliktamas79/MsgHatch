using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// The host of the subscription router tests: the subscription router, the in-memory stores, the embedded test templates,
/// and the message types A to E of the scenario with their channels, plus the message type G with only SignalR.
/// </summary>
public static class SubscriptionHost
{
    public const string TemplatePrefix = "MsgHatch.Subscriptions.Tests.Unit.TemplateFiles";

    public static MsgHatchBuilder AddScenarioHost(
        this IServiceCollection services, Func<TenantKey, ISubscriptionStore> store, Func<TenantKey, IRecipientDirectory> directory) =>
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseSubscriptions()
            .UseInMemoryStores()
            .UseSubscriptionStore((_, key) => store(key))
            .UseRecipientDirectory((_, key) => directory(key))
            .UseEmbeddedTemplates(typeof(SubscriptionHost).Assembly, TemplatePrefix)
            .AddScenarioMessages();

    public static MsgHatchBuilder AddScenarioMessages(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var json = SubscriptionTestJson.Default;
        return builder
            .AddMessage("A", 1, json.EventA, static m => m
                .WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventA>>(static p => p.AsEmail("AEmail")))
            .AddMessage("B", 1, json.EventB, static m => m
                .WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventB>>(static p => p.AsEmail("BEmail")))
            .AddMessage("C", 1, json.EventC, m => m
                .WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventC>>(static p => p.AsEmail("CEmail"))
                .WithPayload<Notice, NoticeLoader<EventC>>(p => p.AsSignalR("CUpdate", 1, json.SignalREnvelopeNotice)))
            .AddMessage("D", 1, json.EventD, m => m
                .WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventD>>(static p => p.AsEmail("DEmail"))
                .WithPayload<Notice, NoticeLoader<EventD>>(p => p
                    .AsSignalR("DUpdate", 1, json.SignalREnvelopeNotice)
                    .AsBroker("DEvent", 1, json.BrokerEnvelopeNotice)))
            .AddMessage("E", 1, json.EventE, m => m
                .WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))
                .WithPayload<NoticeEmail, NoticeEmailLoader<EventE>>(static p => p.AsEmail("EEmail"))
                .WithPayload<Notice, NoticeLoader<EventE>>(p => p.AsBroker("EEvent", 1, json.BrokerEnvelopeNotice)))
            .AddMessage("G", 1, json.EventG, m => m
                .WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))
                .WithPayload<Notice, NoticeLoader<EventG>>(p => p.AsSignalR("GUpdate", 1, json.SignalREnvelopeNotice)));
    }

    public static async Task<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        this IServiceProvider provider, TEvent domainEvent, TenantId? tenant = null, Guid? messageId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(domainEvent, new MessageContext(messageId ?? Guid.NewGuid(), tenant), TestContext.Current.CancellationToken);
    }
}
