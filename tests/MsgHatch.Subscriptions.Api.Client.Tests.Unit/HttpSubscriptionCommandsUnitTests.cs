using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="ISubscriptionCommands"/> on the HTTP client of <c>UseSubscriptionApiClient</c>, against the subscription endpoints.</summary>
public sealed class HttpSubscriptionCommandsUnitTests : SubscriptionCommandsTestsBase<ISubscriptionCommands>
{
    protected override ValueTask<IManagementTestHost<ISubscriptionCommands>> StartAsync(Action<IServiceCollection> addServices) =>
        HttpManagementHost.StartAsync<ISubscriptionCommands>(addServices);
}
