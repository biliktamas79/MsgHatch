using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>The use cases of <see cref="IDeliveryCommands"/> on the in-process delivery manager, the default of <c>AddMsgHatch</c>.</summary>
public sealed class DeliveryManagerCommandsUnitTests : DeliveryCommandsTestsBase<IDeliveryCommands>
{
    protected override ValueTask<IRegistryQueriesTestHost<IDeliveryCommands>> StartAsync(Action<IServiceCollection> addServerServices) =>
        InProcessRegistryQueriesHost.StartAsync<IDeliveryCommands>(addServerServices);
}
