using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="IRoutingVerifier"/> on the HTTP client of <c>UseSubscriptionApiClient</c>, against <c>POST {prefix}/verification</c>.</summary>
public sealed class HttpRoutingVerifierUnitTests : RoutingVerifierTestsBase<IRoutingVerifier>
{
    protected override ValueTask<IManagementTestHost<IRoutingVerifier>> StartAsync(Action<IServiceCollection> addServices) =>
        HttpManagementHost.StartAsync<IRoutingVerifier>(addServices);
}
