using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Api.Contracts;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Api.Client;

/// <summary>
/// The routing verification of one tenant key over HTTP (D163, D232). It goes with the queries, because a verification
/// changes no stored subscription. It runs <see cref="RoutingVerificationRules"/> first (D248). A row has its trace only
/// when this host registers the trace type <c>Trace:Subscription</c>.
/// </summary>
/// <param name="api">The generated client of the routes.</param>
/// <param name="key">The tenant key, whose tenant each request sends as <c>tenantId</c>.</param>
/// <param name="registry">The content type registry of this host, which gives the trace type.</param>
internal sealed class HttpRoutingVerifier(SubscriptionApi api, TenantKey key, IContentTypeRegistry registry) : IRoutingVerifier
{
    public async ValueTask<ErrorOr<RoutingVerificationReport>> VerifyAsync(RoutingVerificationRequest request, CancellationToken ct) =>
        RoutingVerificationRules.Check(request) is { } invalid
            ? invalid
            : ApiResults.One(await api.VerifyAsync(RoutingVerificationBody.From(request), key.Tenant?.Value, ct), report => report.ToReport(registry));
}
