using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="IRegistryCommands"/> on the HTTP client of <c>UseRegistryApiClient</c>, against the registry endpoints.</summary>
public sealed class HttpRegistryCommandsUnitTests : RegistryCommandsTestsBase<IRegistryCommands>
{
    protected override async ValueTask<IRegistryQueriesTestHost<IRegistryCommands>> StartAsync(Action<IServiceCollection> addServerServices) =>
        new CommandsHost((HttpRegistryHost)await HttpRegistryHost.StartAsync(addServerServices));

    /// <summary>The commands of the client host of <see cref="HttpRegistryHost"/>.</summary>
    private sealed class CommandsHost(HttpRegistryHost host) : IRegistryQueriesTestHost<IRegistryCommands>
    {
        public IServiceProvider Server => host.Server;

        public IRegistryCommands Service(TenantKey key) => host.Client.GetRequiredKeyedService<IRegistryCommands>(key);

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
