using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// The migration step of the base package (D320) on SQLite, with the migrations of a host in its own assembly, as a host
/// with no provider package of MsgHatch keeps them (<see cref="SqliteHostMigrations"/>).
/// </summary>
public sealed class EntityFrameworkMigrationExtensionsUnitTests : IDisposable
{
    private const string _tables = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite%' ORDER BY name";

    private readonly SqliteTestDatabase _database = new();

    private readonly SqliteTestDatabase _other = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _database.Delete();
        _other.Delete();
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_EmptyDatabase_CreatesTheTablesAndTheHistoryTableOfTheContext()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(
            [["ChannelEntry"], ["ChannelState"], ["Inbox"], ["MessageTypeEntry"], ["Outbox"], ["__StoreMigrations"]],
            _database.Rows(_tables).Where(static row => row[0] != "__EFMigrationsLock"));
        Assert.Equal([MigrationId()], _database.Rows("SELECT MigrationId FROM __StoreMigrations").Select(static row => row[0]));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_SecondCall_ChangesNothing()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var schema = SchemaOf(_database);

        // Act
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Assert
        Assert.Equal(schema, SchemaOf(_database));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_OwnProviderSetup_MigratesItsOwnDatabaseAndTheStoresKeepTheirs()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString);

        // Act
        await provider.MigrateMsgHatchAsync(o => SqliteHostMigrations.UseSqlite(o, _other.ConnectionString), Ct);

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(
            (6, 0, _database.ConnectionString),
            (_other.Rows(_tables).Count(static row => row[0] != "__EFMigrationsLock"),
                _database.Rows(_tables).Count, context.Database.GetConnectionString()));
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_MigratedDatabase_StoresAndClaimsAMessage()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString);
        await provider.MigrateMsgHatchAsync(ct: Ct);
        var message = TestMessages.AReceivedMessage(new TestEvent("text"));

        // Act
        var error = await provider.GetRequiredService<IInboxStore>().AddAsync(message, Ct);
        var claim = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal((null, message.Delivery.Id), (error, Assert.Single(claim.Value.Messages).Delivery.Id));
    }

    [Fact]
    public async Task HasPendingModelChanges_ModelOfTheContext_HasNoChangeAgainstTheMigrationsOfTheHost()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString);
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }

    [Fact]
    public async Task RequireCurrentSchema_MigrationThatIsNotApplied_FailsTheStartupWithTheContextAndTheMigration()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString, requireCurrentSchema: true);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains(nameof(MsgHatchDbContext), StringComparison.Ordinal), failure.Contains(MigrationId(), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RequireCurrentSchema_EachMigrationApplied_PassesTheStartup()
    {
        // Arrange
        await using var provider = Build(_database.ConnectionString, requireCurrentSchema: true);
        await provider.MigrateMsgHatchAsync(ct: Ct);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    private static string MigrationId() =>
        typeof(SqliteHostMigrations).Assembly.GetTypes().Select(static type => type.GetCustomAttributes(typeof(Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute), false))
            .Where(static attributes => attributes.Length > 0).Select(static attributes => ((Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute)attributes[0]).Id)
            .Single();

    private static List<string?[]> SchemaOf(SqliteTestDatabase database) =>
        [.. database.Rows("SELECT type, name, sql FROM sqlite_master ORDER BY type, name"), .. database.Rows("SELECT * FROM __StoreMigrations")];

    private static ServiceProvider Build(string connectionString, bool requireCurrentSchema = false)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => (o.EnableLogging, o.Roles) = (false, MsgHatchRoles.Consumer))
            .UseEntityFrameworkStores(o => SqliteHostMigrations.UseSqlite(o, connectionString), o => o.RequireCurrentSchema = requireCurrentSchema)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m.WithRouter(static (_, _) => NSubstitute.Substitute.For<MsgHatch.Domain.Routing.IMessageRouter<TestEvent>>()));
        return services.Build();
    }
}
