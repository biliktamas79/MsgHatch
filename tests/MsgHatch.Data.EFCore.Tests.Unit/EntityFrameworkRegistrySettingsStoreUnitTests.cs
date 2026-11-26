using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>The registry settings contract on the EF Core SQLite provider (D318).</summary>
public sealed class EntityFrameworkRegistrySettingsStoreUnitTests : RegistrySettingsStoreTestsBase<EntityFrameworkRegistrySettingsStore>
{
    private readonly SqliteTestDatabase _database;

    public EntityFrameworkRegistrySettingsStoreUnitTests()
        : this(new SqliteTestDatabase())
    {
    }

    private EntityFrameworkRegistrySettingsStoreUnitTests(SqliteTestDatabase database)
        : base(new EntityFrameworkRegistrySettingsStore(database.Contexts), _ => new EntityFrameworkRegistrySettingsStoreTestData(database))
    {
        _database = database;
    }

    public static TheoryData<string, string> InvalidRowRows => new()
    {
        { "invalid JSON", "INSERT INTO MessageTypeEntry VALUES ('A', NULL, NULL, 1, '[not json', 1, '2030-01-01 00:00:00')" },
        { "invalid channel name", """INSERT INTO MessageTypeEntry VALUES ('A', NULL, NULL, 1, '["not a name!"]', 1, '2030-01-01 00:00:00')""" },
        { "invalid message type name", "INSERT INTO MessageTypeEntry VALUES ('not a name!', NULL, NULL, 1, '[]', 1, '2030-01-01 00:00:00')" },
        { "invalid channel entry name", "INSERT INTO ChannelEntry VALUES ('not a name!', NULL, NULL, 1, 1, '2030-01-01 00:00:00')" },
    };

    [Theory]
    [MemberData(nameof(InvalidRowRows))]
    public async Task GetEntriesAsync_RowThatAPersonChangedIntoAnInvalidValue_GivesSerializationFailed(string row, string insert)
    {
        // Arrange
        _database.Execute(insert);

        // Act
        var entries = await Store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, entries.Error?.Type));
    }

    [Fact]
    public async Task GetEntriesAsync_RowThatAPersonChangedAndADialectThatMapsEachDatabaseException_GivesSerializationFailed()
    {
        // Arrange
        var store = new EntityFrameworkRegistrySettingsStore(
            _database.ContextsOf<MsgHatchDbContext>(o => o.UseEntityFrameworkStoreDialect(new SqliteTestDialect(errorType: Errors.Rejected))));
        _database.Execute("INSERT INTO ChannelEntry VALUES ('not a name!', NULL, NULL, 1, 1, '2030-01-01 00:00:00')");

        // Act
        var entries = await store.GetEntriesAsync(Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, entries.Error?.Type);
    }

    [Fact]
    public async Task SaveChannelEntryAsync_DatabaseThatDoesNotExist_GivesUnavailable()
    {
        // Arrange
        var missingDatabase = new FailingConnectionInterceptor();
        var store = new EntityFrameworkRegistrySettingsStore(new SqliteTestDatabase().ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(missingDatabase)));

        // Act
        var error = await store.SaveChannelEntryAsync(AChannelEntry(), Ct);

        // Assert
        Assert.Equal((Errors.Unavailable, 1), (error?.Type, missingDatabase.Opens));
    }

    [Fact]
    public async Task SaveChannelEntryAsync_TableThatDoesNotExist_GivesUnavailable()
    {
        // Arrange
        _database.Execute("DROP TABLE ChannelEntry");

        // Act
        var error = await Store.SaveChannelEntryAsync(AChannelEntry(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
