using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.Sqlite.Tests.Unit;

public sealed class SqliteTenantStoreOptionsValidatorUnitTests
{
    private const string _tenantConnectionString = nameof(SqliteTenantStoreOptions.TenantConnectionString);

    private const string _systemConnectionString = nameof(SqliteTenantStoreOptions.SystemConnectionString);

    private const string _validTenant = "Data Source=data/msghatch-{tenant}.db";

    private const string _validSystem = "Data Source=data/msghatch.db";

    public static TheoryData<string?, string[]> TenantConnectionStringRows => new()
    {
        { null, [_tenantConnectionString] },
        { "", [_tenantConnectionString] },
        { "   ", [_tenantConnectionString] },
        { "Data Source=data/msghatch.db", [_tenantConnectionString] },
        { "Data Source=data/msghatch-{Tenant}.db", [_tenantConnectionString] },
        { _validTenant, [] },
        { "Data Source=C:\\Ödön\\üzenetek & <x>-{tenant}.db", [] },
    };

    public static TheoryData<string?, string[]> SystemConnectionStringRows => new()
    {
        { null, [_systemConnectionString] },
        { "", [_systemConnectionString] },
        { "   ", [_systemConnectionString] },
        { _validSystem, [] },
        { "Data Source=C:\\Ödön\\üzenetek & <x>.db", [] },
        { "Data Source=data/msghatch-{tenant}.db", [_systemConnectionString] },
    };

    [Theory]
    [MemberData(nameof(TenantConnectionStringRows))]
    public void Validate_TenantConnectionString_FailsWhenItIsMissingOrHasNoTenantPlaceholder(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new SqliteTenantStoreOptions { TenantConnectionString = value!, SystemConnectionString = _validSystem };

        // Act
        var result = new SqliteTenantStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }

    [Theory]
    [MemberData(nameof(SystemConnectionStringRows))]
    public void Validate_SystemConnectionString_FailsWhenItIsMissingOrHasTheTenantPlaceholder(string? value, string[] expectedFailedProperties)
    {
        // Arrange
        var options = new SqliteTenantStoreOptions { TenantConnectionString = _validTenant, SystemConnectionString = value! };

        // Act
        var result = new SqliteTenantStoreOptionsValidator().Validate(null, options);

        // Assert
        Assert.Equal(expectedFailedProperties, OptionsFailures.FailedProperties(result));
    }
}
