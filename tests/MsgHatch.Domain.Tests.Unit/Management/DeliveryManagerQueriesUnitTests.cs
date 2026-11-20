using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>The use cases of <see cref="IDeliveryQueries"/> on the in-process delivery manager, the default of <c>AddMsgHatch</c>.</summary>
public sealed class DeliveryManagerQueriesUnitTests : DeliveryQueriesTestsBase<IDeliveryQueries>
{
    protected override ValueTask<IRegistryQueriesTestHost<IDeliveryQueries>> StartAsync(Action<IServiceCollection> addServerServices) =>
        InProcessRegistryQueriesHost.StartAsync<IDeliveryQueries>(addServerServices);
}
