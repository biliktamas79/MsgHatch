using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.Api.Client;

/// <summary>
/// The subscription queries of one tenant key over HTTP (D227, D231). A channel descriptor and the settings of a
/// subscription get the settings type of the entry <c>Settings:{channel}</c> of the registry of this host, or
/// <c>JsonChannelSettings</c> when the host registers none (D229).
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant each request sends as <c>tenantId</c>.</param>
/// <param name="registry">The content type registry of this host.</param>
internal sealed class HttpSubscriptionQueries(SubscriptionApi api, TenantKey key, IContentTypeRegistry registry) : ISubscriptionQueries
{
    private string? Tenant => key.Tenant?.Value;

    public async ValueTask<ErrorOr<IReadOnlyList<ChannelDescriptor>>> GetChannelsAsync(CancellationToken ct) =>
        ApiResults.All(await api.GetChannelsAsync(Tenant, ct), channel => channel.ToChannelDescriptor(registry));

    public async ValueTask<ErrorOr<IReadOnlyList<MessageTypeState>>> GetMessageTypesAsync(MessageTypeQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ApiResults.All(
            await api.GetMessageTypesAsync(query.Type?.Name, query.Company?.Value, query.User?.Value, Tenant, ct),
            state => state.ToMessageTypeState(registry));
    }

    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>>> GetSubscriptionsAsync(SubscriptionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ApiResults.All(
            await api.GetSubscriptionsAsync(query.Type?.Name, query.Company?.Value, query.User?.Value, query.Level, Tenant, ct),
            subscription => subscription.ToSubscription(registry));
    }

    public async ValueTask<ErrorOr<Subscription>> GetSubscriptionAsync(Guid id, CancellationToken ct) =>
        ApiResults.One(await api.GetSubscriptionAsync(id, Tenant, ct), subscription => subscription.ToSubscription(registry));
}
