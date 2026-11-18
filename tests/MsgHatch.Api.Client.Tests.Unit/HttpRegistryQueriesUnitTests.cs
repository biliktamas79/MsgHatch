using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>The use cases of <see cref="IRegistryQueries"/> on the HTTP client of <c>UseRegistryApiClient</c>, against the registry endpoints.</summary>
public sealed class HttpRegistryQueriesUnitTests : RegistryQueriesTestsBase<IRegistryQueries>
{
    protected override ValueTask<IRegistryQueriesTestHost<IRegistryQueries>> StartAsync(Action<IServiceCollection> addServerServices) =>
        HttpRegistryHost.StartAsync(addServerServices);
}
