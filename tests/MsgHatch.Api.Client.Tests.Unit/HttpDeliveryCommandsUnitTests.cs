using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="IDeliveryCommands"/> on the HTTP client of <c>UseDeliveryApiClient</c>, against the delivery endpoints.</summary>
public sealed class HttpDeliveryCommandsUnitTests : DeliveryCommandsTestsBase<IDeliveryCommands>
{
    protected override async ValueTask<IRegistryQueriesTestHost<IDeliveryCommands>> StartAsync(Action<IServiceCollection> addServerServices) =>
        new CommandsHost(await HttpRegistryHost.StartAsync(addServerServices, static (builder, http) => builder.UseDeliveryApiClient(HttpRegistryHost.BaseAddress, http)));

    /// <summary>The delivery commands of the client host of <see cref="HttpRegistryHost"/>.</summary>
    private sealed class CommandsHost(HttpRegistryHost host) : IRegistryQueriesTestHost<IDeliveryCommands>
    {
        public IServiceProvider Server => host.Server;

        public IDeliveryCommands Service(TenantKey key) => host.Client.GetRequiredKeyedService<IDeliveryCommands>(key);

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
