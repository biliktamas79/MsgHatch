using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>The use cases of <see cref="IRegistryCommands"/> on the in-process registry manager, the default of <c>AddMsgHatch</c>.</summary>
public sealed class RegistryManagerCommandsUnitTests : RegistryCommandsTestsBase<IRegistryCommands>
{
    protected override ValueTask<IRegistryQueriesTestHost<IRegistryCommands>> StartAsync(Action<IServiceCollection> addServerServices) =>
        InProcessRegistryQueriesHost.StartAsync<IRegistryCommands>(addServerServices);
}
