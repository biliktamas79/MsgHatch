using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>The test data of the EF Core registry settings store on MySQL. A reset empties the tables (<see cref="MySqlTestDatabase.ResetAsync"/>).</summary>
public sealed class MySqlRegistrySettingsStoreTestData(MySqlTestDatabase database) : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);
}
