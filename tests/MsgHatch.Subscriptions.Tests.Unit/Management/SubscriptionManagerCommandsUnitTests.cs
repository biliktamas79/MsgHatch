using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Management;

/// <summary>The use cases of <see cref="ISubscriptionCommands"/> on the in-process manager of <c>UseSubscriptions</c>.</summary>
public sealed class SubscriptionManagerCommandsUnitTests : SubscriptionCommandsTestsBase<ISubscriptionCommands>
{
    protected override ValueTask<IManagementTestHost<ISubscriptionCommands>> StartAsync(Action<IServiceCollection> addServices) =>
        InProcessManagementHost.StartAsync<ISubscriptionCommands>(addServices);
}
