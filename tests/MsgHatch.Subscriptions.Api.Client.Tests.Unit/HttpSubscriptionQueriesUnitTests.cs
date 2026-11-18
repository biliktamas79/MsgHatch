using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="ISubscriptionQueries"/> on the HTTP client of <c>UseSubscriptionApiClient</c>, against the subscription endpoints.</summary>
public sealed class HttpSubscriptionQueriesUnitTests : SubscriptionQueriesTestsBase<ISubscriptionQueries>
{
    protected override ValueTask<IManagementTestHost<ISubscriptionQueries>> StartAsync(Action<IServiceCollection> addServices) =>
        HttpManagementHost.StartAsync<ISubscriptionQueries>(addServices);
}
