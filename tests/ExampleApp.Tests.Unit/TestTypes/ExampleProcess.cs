using ExampleApp.Recipients;
using ExampleApp.Startup;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Resolution;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the tests that run events through a container of the example: the seed of one tenant, the process of an
/// event, and the outgoing messages that the outbox stores, read with a claim of each channel.
/// </summary>
public static class ExampleProcess
{
    /// <summary>Writes the shared scenario into the directory and the subscription store of one tenant.</summary>
    public static async Task SeedScenarioAsync(this IServiceProvider provider, TenantKey key)
    {
        await using var scope = provider.CreateAsyncScope();
        await ExampleStartup.SeedAsync(DirectoryOf(scope.ServiceProvider, key), ManagerOf(scope.ServiceProvider, key), Ct);
    }

    public static ExampleRecipientDirectory DirectoryOf(IServiceProvider provider, TenantKey key) =>
        ActivatorUtilities.CreateInstance<ExampleRecipientDirectory>(provider, key);

    public static ISubscriptionManager ManagerOf(IServiceProvider provider, TenantKey key) =>
        provider.GetRequiredKeyedService<ISubscriptionManager>(key);

    public static async Task<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(this IServiceProvider provider, TEvent domainEvent, TenantId? tenant = null)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(domainEvent, new MessageContext(Guid.NewGuid(), tenant), Ct);
    }

    public static async Task<ErrorOr<Guid>> AcceptAsync<TEvent>(this IServiceProvider provider, TEvent domainEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().AcceptAsync(domainEvent, new MessageContext(Guid.NewGuid()), Ct);
    }

    /// <summary>
    /// The outgoing messages of the email, SignalR, and broker channels as text, in the order of the text: an email as
    /// <c>{tenant} email {to} {html|text} {subject}</c>, and another message as <c>{tenant} {channel} {u|c}:{id}
    /// {language source} {message type}</c>.
    /// </summary>
    public static async Task<IReadOnlyList<string>> StoredAsync(this IServiceProvider provider, DateTime nowUtc)
    {
        var store = provider.GetRequiredService<IOutboxDispatchStore>();
        List<string> stored = [];
        foreach (var channel in new[] { EmailChannel.Id, SignalRChannel.Id, BrokerChannel.Id })
        {
            var claim = await store.ClaimDueAsync(channel, 1000, nowUtc, TimeSpan.FromMinutes(1), Ct);
            stored.AddRange(claim.Value.Messages.Select(Describe));
        }

        return [.. stored.Order(StringComparer.Ordinal)];
    }

    private static string Describe(OutgoingMessage message) => message.Content switch
    {
        EmailMessage email => $"{message.Tenant?.Value ?? "-"} email {email.To} {(email.HtmlBody is null ? "text" : "html")} {email.Subject}",
        _ => $"{message.Tenant?.Value ?? "-"} {message.Channel.Name} {(message.Target.Kind == DeliveryTargetKind.User ? "u" : "c")}:{message.Target.Id} "
            + $"{((ResolutionTrace)message.Trace!).Language} {message.Type.Name}",
    };
}
