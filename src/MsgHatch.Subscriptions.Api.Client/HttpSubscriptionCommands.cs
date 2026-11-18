using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Api.Contracts;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.Api.Client;

/// <summary>
/// The subscription commands of one tenant key over HTTP (D227, D231). Each create and update runs
/// <see cref="SubscriptionDraftRules"/> first, so a draft that breaks a rule gives its error with no HTTP call (D234). A
/// create is not idempotent, so a host does not retry a failed create.
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant each request sends as <c>tenantId</c>.</param>
/// <param name="registry">The content type registry of this host, which gives the JSON of the settings.</param>
internal sealed class HttpSubscriptionCommands(SubscriptionApi api, TenantKey key, IContentTypeRegistry registry) : ISubscriptionCommands
{
    private string? Tenant => key.Tenant?.Value;

    public async ValueTask<ErrorOr<Subscription>> CreateAsync(SubscriptionDraft draft, CancellationToken ct) =>
        SubscriptionDraftRules.Check(draft) is { } invalid
            ? invalid
            : ApiResults.One(await api.CreateAsync(SubscriptionRequest.From(draft, registry), Tenant, ct), subscription => subscription.ToSubscription(registry));

    public async ValueTask<ErrorOr<Subscription>> UpdateAsync(Guid id, int expectedRevision, SubscriptionDraft draft, CancellationToken ct) =>
        SubscriptionDraftRules.Check(draft) is { } invalid
            ? invalid
            : ApiResults.One(
                await api.UpdateAsync(id, SubscriptionRequest.From(draft, registry, expectedRevision), Tenant, ct),
                subscription => subscription.ToSubscription(registry));

    public async ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct) => await api.DeleteAsync(id, Tenant, ct);
}
