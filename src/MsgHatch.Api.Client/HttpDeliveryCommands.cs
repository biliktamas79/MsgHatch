using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Client;

/// <summary>
/// The delivery commands of one tenant key over HTTP (D227, D289, D294). A command runs <see cref="DeliveryRules"/> first, and
/// gives the error of a rule with no HTTP call (D292). A bulk action sends the tenant of the key as <c>tenantId</c>. The
/// pause, the resume, the delete of the test data, and the purge act on all tenants, so they send no <c>tenantId</c>, and
/// their rules give <see cref="Errors.ValidationFailed"/> for the key of a tenant.
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant a bulk action sends as <c>tenantId</c>.</param>
internal sealed class HttpDeliveryCommands(DeliveryApi api, TenantKey key) : IDeliveryCommands
{
    private string? Tenant => key.Tenant?.Value;

    public async ValueTask<Error?> PauseChannelAsync(ChannelId channel, string? note, CancellationToken ct) =>
        DeliveryRules.CheckPause(key, note) ?? await api.PauseChannelAsync(channel.Name, new PauseRequest(note), ct);

    public async ValueTask<Error?> ResumeChannelAsync(ChannelId channel, CancellationToken ct) =>
        DeliveryRules.CheckGlobalAction(key) ?? await api.ResumeChannelAsync(channel.Name, ct);

    public async ValueTask<ErrorOr<DeliveryActionResult>> RetryReceivedAsync(InboxQuery query, CancellationToken ct) =>
        DeliveryRules.CheckBulkAction(query) is { } invalid ? invalid : await api.RetryReceivedAsync(InboxFilterRequest.From(query), Tenant, ct);

    public async ValueTask<ErrorOr<DeliveryActionResult>> CancelReceivedAsync(InboxQuery query, CancellationToken ct) =>
        DeliveryRules.CheckBulkAction(query) is { } invalid ? invalid : await api.CancelReceivedAsync(InboxFilterRequest.From(query), Tenant, ct);

    public async ValueTask<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync(OutboxQuery query, CancellationToken ct) =>
        DeliveryRules.CheckBulkAction(query) is { } invalid ? invalid : await api.RetryOutgoingAsync(OutboxFilterRequest.From(query), Tenant, ct);

    public async ValueTask<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync(OutboxQuery query, CancellationToken ct) =>
        DeliveryRules.CheckBulkAction(query) is { } invalid ? invalid : await api.CancelOutgoingAsync(OutboxFilterRequest.From(query), Tenant, ct);

    public async ValueTask<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct) =>
        DeliveryRules.CheckGlobalAction(key) is { } invalid ? invalid : await api.DeleteTestDataAsync(ct);

    public async ValueTask<ErrorOr<DeletedMessages>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        DeliveryRules.CheckPurge(key, maxCount) is { } invalid ? invalid : await api.PurgeAsync(new PurgeRequest(finishedBeforeUtc, maxCount), ct);
}
