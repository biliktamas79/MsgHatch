using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UsePostgreSqlDispatchSignal</c> and its startup checks (section 4.7, D62, D190, D334). The startup validation opens
/// no connection, so its tests use a connection string with a closed port. The tests of the database of the signal use a
/// listener in the test process that closes each connection.
/// </summary>
public sealed class PostgreSqlDispatchSignalExtensionsUnitTests
{
    private const string _connectionString = "Host=127.0.0.1;Port=1;Database=signal";

    private static readonly DispatchTopic _topic = DispatchTopic.ForChannel(TestChannel.Id);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_StoresOnPostgreSql_PassesTheStartupValidation()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkPostgreSqlStores(_connectionString).UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_InboxInTheTransactionOfTheHost_PassesTheStartupValidation()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContext>(static o => o.UseNpgsql(_connectionString));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkPostgreSqlStores<TestHostContext>(_connectionString).UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_NoEntityFrameworkStores_FailsTheStartupValidation()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores().UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        AssertStoresNotOnPostgreSql(exception);
    }

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_EntityFrameworkStoresOnSqlite_FailsTheStartupValidation()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkStores(static o => o.UseSqlite("Data Source=:memory:")).UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        AssertStoresNotOnPostgreSql(exception);
    }

    [Theory]
    [InlineData("Inbox")]
    [InlineData("Outbox")]
    public async Task UsePostgreSqlDispatchSignal_NoPollingForOneDispatcher_FailsTheStartupValidationForItsOptions(string dispatcher)
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkPostgreSqlStores(_connectionString).UsePostgreSqlDispatchSignal()
            .Services.Configure<DispatchOptions>(dispatcher, static o => o.EnablePolling = false);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal((typeof(DispatchOptions), dispatcher), (failure.OptionsType, failure.OptionsName));
        Assert.StartsWith("UsePostgreSqlDispatchSignal needs EnablePolling ", Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_AfterUseDispatchSignal_FailsTheStartupValidationWithBothSetupMethods()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkPostgreSqlStores(_connectionString)
            .UseDispatchSignal(static _ => new InProcessDispatchSignal())
            .UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseDispatchSignal and UsePostgreSqlDispatchSignal select ", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_StoresOnALocalListener_ListensOnTheDatabaseOfTheStores()
    {
        // Arrange
        using var database = new ClosingListener();
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkPostgreSqlStores($"Host=127.0.0.1;Port={database.Port};Database=signal").UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var error = await CompletedAsync(provider.GetRequiredService<IDispatchSignal>().WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal((Errors.Unavailable, true), (error?.Type, database.Connections > 0));
    }

    [Fact]
    public async Task UsePostgreSqlDispatchSignal_InboxInTheTransactionOfTheHostOnALocalListener_ListensOnTheDatabaseOfTheStores()
    {
        // Arrange
        using var database = new ClosingListener();
        var connectionString = $"Host=127.0.0.1;Port={database.Port};Database=signal";
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContext>(o => o.UseNpgsql(connectionString));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkPostgreSqlStores<TestHostContext>(connectionString).UsePostgreSqlDispatchSignal();
        await using var provider = services.Build();

        // Act
        var error = await CompletedAsync(provider.GetRequiredService<IDispatchSignal>().WaitAsync(_topic, Ct));

        // Assert
        Assert.Equal((Errors.Unavailable, true), (error?.Type, database.Connections > 0));
    }

    private static Task<Error?> CompletedAsync(ValueTask<Error?> wait) =>
        wait.AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

    private static void AssertStoresNotOnPostgreSql(Exception? exception) =>
        Assert.StartsWith(
            "UsePostgreSqlDispatchSignal needs the EF Core stores on PostgreSQL",
            Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures),
            StringComparison.Ordinal);
}
