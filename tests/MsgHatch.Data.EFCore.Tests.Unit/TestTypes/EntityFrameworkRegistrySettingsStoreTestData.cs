using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>The test data of the EF Core registry settings store on SQLite. A reset makes a new file with the tables.</summary>
public sealed class EntityFrameworkRegistrySettingsStoreTestData(SqliteTestDatabase database) : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(database.Contexts, ct);
}
