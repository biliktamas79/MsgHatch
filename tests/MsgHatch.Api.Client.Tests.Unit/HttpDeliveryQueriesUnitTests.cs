using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="IDeliveryQueries"/> on the HTTP client of <c>UseDeliveryApiClient</c>, against the delivery endpoints.</summary>
public sealed class HttpDeliveryQueriesUnitTests : DeliveryQueriesTestsBase<IDeliveryQueries>
{
    protected override async ValueTask<IRegistryQueriesTestHost<IDeliveryQueries>> StartAsync(Action<IServiceCollection> addServerServices) =>
        new QueriesHost(await HttpRegistryHost.StartAsync(addServerServices, static (builder, http) => builder.UseDeliveryApiClient(HttpRegistryHost.BaseAddress, http)));

    /// <summary>The delivery queries of the client host of <see cref="HttpRegistryHost"/>.</summary>
    private sealed class QueriesHost(HttpRegistryHost host) : IRegistryQueriesTestHost<IDeliveryQueries>
    {
        public IServiceProvider Server => host.Server;

        public IDeliveryQueries Service(TenantKey key) => host.Client.GetRequiredKeyedService<IDeliveryQueries>(key);

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
