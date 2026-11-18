using MsgHatch.Domain.Failures;
using MsgHatch.Http;
using MsgHatch.Subscriptions.Api.Contracts;

namespace MsgHatch.Subscriptions.Api.Client;

/// <summary>
/// The routes of the subscription management API with no <c>/me</c> (section 4.17, D163). A <c>/me</c> route narrows each
/// query to the signed-in user, and a browser calls it (D154). The generator writes <c>SubscriptionApi</c>, which sends each
/// call and gives each failure as an <see cref="Error"/> (D233, D236). Each route takes the optional query value <c>tenantId</c>.
/// </summary>
[HttpApi(typeof(SubscriptionEndpointJson))]
internal interface ISubscriptionApi
{
    [HttpCall("GET", SubscriptionApiRoutes.Channels)]
    Task<ErrorOr<IReadOnlyList<ChannelResponse>>> GetChannelsAsync(string? tenantId, CancellationToken ct);

    [HttpCall("GET", SubscriptionApiRoutes.MessageTypes)]
    Task<ErrorOr<IReadOnlyList<MessageTypeResponse>>> GetMessageTypesAsync(
        string? messageType, string? companyId, string? userId, string? tenantId, CancellationToken ct);

    [HttpCall("GET", SubscriptionApiRoutes.Subscriptions)]
    Task<ErrorOr<IReadOnlyList<SubscriptionResponse>>> GetSubscriptionsAsync(
        string? messageType, string? companyId, string? userId, SubscriptionLevel? level, string? tenantId, CancellationToken ct);

    [HttpCall("GET", SubscriptionApiRoutes.Subscription)]
    Task<ErrorOr<SubscriptionResponse>> GetSubscriptionAsync(Guid id, string? tenantId, CancellationToken ct);

    [HttpCall("POST", SubscriptionApiRoutes.Subscriptions)]
    Task<ErrorOr<SubscriptionResponse>> CreateAsync([HttpBody] SubscriptionRequest body, string? tenantId, CancellationToken ct);

    [HttpCall("PUT", SubscriptionApiRoutes.Subscription)]
    Task<ErrorOr<SubscriptionResponse>> UpdateAsync(Guid id, [HttpBody] SubscriptionRequest body, string? tenantId, CancellationToken ct);

    [HttpCall("DELETE", SubscriptionApiRoutes.Subscription)]
    Task<Error?> DeleteAsync(Guid id, string? tenantId, CancellationToken ct);

    [HttpCall("POST", SubscriptionApiRoutes.Verification)]
    Task<ErrorOr<RoutingVerificationResponse>> VerifyAsync([HttpBody] RoutingVerificationBody body, string? tenantId, CancellationToken ct);
}
