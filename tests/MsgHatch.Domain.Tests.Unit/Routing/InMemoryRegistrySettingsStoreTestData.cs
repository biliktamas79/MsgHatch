using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>The test data of the in-memory registry settings store. Each test gets a new store, so a reset has nothing to do.</summary>
public sealed class InMemoryRegistrySettingsStoreTestData : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => ValueTask.CompletedTask;
}
