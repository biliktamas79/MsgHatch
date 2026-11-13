using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>The test data of the EF Core registry settings store. A reset deletes the rows.</summary>
public sealed class EntityFrameworkRegistrySettingsStoreTestData(TestDatabase database) : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);
}
