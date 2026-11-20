using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Http;

namespace MsgHatch.Api.Client;

/// <summary>
/// The routes of the two route groups of the delivery API under one prefix (section 4.19, D291, D294). The generator writes
/// <c>DeliveryApi</c>, which sends each call and gives each failure as an <see cref="Error"/> (D233, D236). The name of each
/// query value is a name of <c>DeliveryApiRoutes.Query</c>. The routes that act on all tenants take no <c>tenantId</c>.
/// </summary>
[HttpApi(typeof(DeliveryEndpointJson))]
internal interface IDeliveryApi
{
    [HttpCall("GET", DeliveryApiRoutes.Channels)]
    Task<ErrorOr<IReadOnlyList<ChannelStatusResponse>>> GetChannelsAsync(string? tenantId, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.ChannelPause)]
    Task<Error?> PauseChannelAsync(string channel, [HttpBody] PauseRequest body, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.ChannelResume)]
    Task<Error?> ResumeChannelAsync(string channel, CancellationToken ct);

    [HttpCall("GET", DeliveryApiRoutes.Traffic)]
    Task<ErrorOr<TrafficReportResponse>> GetTrafficAsync(
        DateTime? createdFrom, DateTime? createdTo, DeliveryStatus? status, bool includeTest, string? tenantId, CancellationToken ct);

    [HttpCall("GET", DeliveryApiRoutes.Inbox)]
    Task<ErrorOr<MessagePageResponse<ReceivedMessageSummaryResponse>>> GetReceivedMessagesAsync(
        IReadOnlyList<Guid>? ids, DeliveryStatus? status, string? messageType, DateTime? createdFrom, DateTime? createdTo, DateTime? dueBefore,
        bool includeTest, int limit, string? after, string? tenantId, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.InboxRetry)]
    Task<ErrorOr<DeliveryActionResult>> RetryReceivedAsync([HttpBody] InboxFilterRequest body, string? tenantId, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.InboxCancel)]
    Task<ErrorOr<DeliveryActionResult>> CancelReceivedAsync([HttpBody] InboxFilterRequest body, string? tenantId, CancellationToken ct);

    [HttpCall("GET", DeliveryApiRoutes.Outbox)]
    Task<ErrorOr<MessagePageResponse<OutgoingMessageSummaryResponse>>> GetOutgoingMessagesAsync(
        IReadOnlyList<Guid>? ids, DeliveryStatus? status, string? messageType, string? channel, DeliveryTargetKind? targetKind, string? targetId,
        Guid? sourceMessageId, DateTime? createdFrom, DateTime? createdTo, DateTime? dueBefore, bool includeTest, int limit, string? after,
        string? tenantId, CancellationToken ct);

    [HttpCall("GET", DeliveryApiRoutes.OutgoingMessage)]
    Task<ErrorOr<OutgoingMessageDetailResponse>> GetOutgoingMessageAsync(Guid id, string? tenantId, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.OutboxRetry)]
    Task<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync([HttpBody] OutboxFilterRequest body, string? tenantId, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.OutboxCancel)]
    Task<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync([HttpBody] OutboxFilterRequest body, string? tenantId, CancellationToken ct);

    [HttpCall("POST", DeliveryApiRoutes.Purge)]
    Task<ErrorOr<DeletedMessages>> PurgeAsync([HttpBody] PurgeRequest body, CancellationToken ct);

    [HttpCall("DELETE", DeliveryApiRoutes.TestData)]
    Task<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct);

    [HttpCall("GET", DeliveryApiRoutes.ReceivedContent)]
    Task<ErrorOr<JsonElement>> GetReceivedContentAsync(Guid id, string? tenantId, CancellationToken ct);

    [HttpCall("GET", DeliveryApiRoutes.OutgoingContent)]
    Task<ErrorOr<JsonElement>> GetOutgoingContentAsync(Guid id, string? tenantId, CancellationToken ct);
}
