using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Tests.Unit.Verification;

/// <summary>The use cases of <see cref="IRoutingVerifier"/> on the in-process verifier of <c>UseSubscriptions</c>.</summary>
public sealed class RoutingVerifierUnitTests : RoutingVerifierTestsBase<IRoutingVerifier>
{
    protected override ValueTask<IManagementTestHost<IRoutingVerifier>> StartAsync(Action<IServiceCollection> addServices) =>
        InProcessManagementHost.StartAsync<IRoutingVerifier>(addServices);
}
