using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkStores&lt;TContext&gt;</c> on the EF Core SQLite provider (D331, D332): <c>AcceptAsync</c> stages the
/// inbox row in the scoped context of the host, the save of the host writes it with the rows of the host, and the startup
/// validation checks the context of the host.
/// </summary>
public sealed partial class EntityFrameworkStoreExtensionsUnitTests
{
    private const string _hostSetup = "UseEntityFrameworkStores<TestHostContext> needs ";

    private readonly IMessageRouter<TestEvent> _router = Substitute.For<IMessageRouter<TestEvent>>();

    [Fact]
    public async Task AcceptAsync_NoSaveOfTheHost_TracksTheInboxRowAsAddedAndStoresNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();
        await using var scope = provider.CreateAsyncScope();
        var context = new MessageContext(Guid.NewGuid());

        // Act
        var accepted = await scope.ServiceProvider.GetRequiredService<INotificationService>().AcceptAsync(new TestEvent("text"), context, Ct);

        // Assert
        Assert.Equal(context.MessageId, accepted.Value);
        Assert.Equal([("Inbox", EntityState.Added)], TrackedRows(scope));
        Assert.Equal([["0"]], _database.Rows("SELECT COUNT(*) FROM Inbox"));
    }

    [Fact]
    public async Task AcceptAsync_ThenSaveOfTheHost_StoresTheRowOfTheHostAndTheInboxRow()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();
        await using var scope = provider.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<TestHostContext>();
        var context = new MessageContext(Guid.NewGuid());
        host.Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });
        await scope.ServiceProvider.GetRequiredService<INotificationService>().AcceptAsync(new TestEvent("text"), context, Ct);

        // Act
        await host.SaveChangesAsync(Ct);

        // Assert
        Assert.Equal(
            [["order", $"{context.MessageId:D}", "Pending"]],
            _database.Rows("SELECT o.text, i.Id, i.Status FROM Orders o CROSS JOIN Inbox i"));
    }

    [Fact]
    public async Task AcceptAsync_ThenRollbackOfTheTransactionOfTheHost_StoresNeitherRow()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();
        await using var scope = provider.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<TestHostContext>();
        await using var transaction = await host.Database.BeginTransactionAsync(Ct);
        host.Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });
        await scope.ServiceProvider.GetRequiredService<INotificationService>().AcceptAsync(new TestEvent("text"), new MessageContext(Guid.NewGuid()), Ct);
        await host.SaveChangesAsync(Ct);

        // Act
        await transaction.RollbackAsync(Ct);

        // Assert
        Assert.Equal([["0", "0"]], _database.Rows("SELECT (SELECT COUNT(*) FROM Orders), (SELECT COUNT(*) FROM Inbox)"));
    }

    [Fact]
    public async Task AcceptAsync_IdThatIsStored_FailsTheSaveOfTheHostAndStoresNoRowOfTheHost()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();
        var context = new MessageContext(Guid.NewGuid());
        await AcceptAndSaveAsync(provider, context);

        // Act
        var exception = await Record.ExceptionAsync(() => AcceptAndSaveAsync(provider, context, new TestOrder { Text = "again", CreatedAtUtc = Now }));

        // Assert
        Assert.IsType<DbUpdateException>(exception);
        Assert.Equal([["0", "1"]], _database.Rows("SELECT (SELECT COUNT(*) FROM Orders), (SELECT COUNT(*) FROM Inbox)"));
    }

    [Fact]
    public async Task AcceptAsync_SameIdTwiceInOneScope_StagesOneInboxRow()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var context = new MessageContext(Guid.NewGuid());
        await service.AcceptAsync(new TestEvent("first"), context, Ct);

        // Act
        var accepted = await service.AcceptAsync(new TestEvent("second"), context, Ct);

        // Assert
        Assert.Equal(context.MessageId, accepted.Value);
        Assert.Equal([("Inbox", EntityState.Added)], TrackedRows(scope));
    }

    [Fact]
    public async Task ProcessAsync_InboxInTheTransactionOfTheHost_StoresTheOutgoingMessageThroughTheStoreContexts()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();
        await using var scope = provider.CreateAsyncScope();
        _router.RouteAsync(Arg.Any<ReceivedMessage<TestEvent>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([TestRoutes.ARoute()])));

        // Act
        var result = await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessAsync(new TestEvent("text"), new MessageContext(Guid.NewGuid()), Ct);

        // Assert
        Assert.Equal(1, result.Value.Stored);
        Assert.Equal([["1", "0"]], _database.Rows("SELECT (SELECT COUNT(*) FROM Outbox), (SELECT COUNT(*) FROM Inbox)"));
        Assert.Empty(TrackedRows(scope));
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_OptionsOfTheStores_MapTheInboxOfTheHostAsTheStoreContextsMapIt()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(
            configureOptions: static o => (o.Schema, o.TextCollation, o.JsonColumnType) = ("other", "NOCASE", "json"));
        await using var scope = provider.CreateAsyncScope();
        await using var store = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        var expected = TableDescriptions.Of(store, "Inbox");

        // Act
        var table = TableDescriptions.Of(scope.ServiceProvider.GetRequiredService<TestHostContext>(), "Inbox");

        // Assert
        Assert.Equal(expected, table);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_ScopedContextOnTheDatabaseOfTheStores_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_NoContextOfTheHost_FailsTheStartupValidation()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(static _ => { }, createTables: false);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith($"{_hostSetup}TestHostContext as a scoped service, for example", HostFailureOf(exception), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(ServiceLifetime.Singleton)]
    public async Task UseEntityFrameworkStoresOfTheHost_ContextThatIsNotScoped_FailsTheStartupValidation(ServiceLifetime lifetime)
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(
            services => services.AddDbContext<TestHostContext>(o => o.UseSqlite(_database.ConnectionString), lifetime, ServiceLifetime.Singleton), createTables: false);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith($"{_hostSetup}TestHostContext as a scoped service, so that", HostFailureOf(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_ProviderThatIsNotRelational_FailsTheStartupValidation()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(
            static services => services.AddDbContext<TestHostContext>(static o => o.UseInMemoryDatabase("host")), createTables: false);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith($"{_hostSetup}a relational EF Core provider", HostFailureOf(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_ModelWithNoInbox_FailsTheStartupValidationAndNamesMapMsgHatchInbox()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContextWithNoInbox>(o => o.UseSqlite(_database.ConnectionString));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkStores<TestHostContextWithNoInbox>(o => o.UseSqlite(_database.ConnectionString));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith(
            "UseEntityFrameworkStores<TestHostContextWithNoInbox> needs the inbox in the model of TestHostContextWithNoInbox: call modelBuilder.MapMsgHatchInbox(this)",
            Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_ContextOnAnotherDatabase_FailsTheStartupValidation()
    {
        // Arrange
        var other = new SqliteTestDatabase();
        await using var provider = await BuildWithHostContextAsync(
            services => services.AddDbContext<TestHostContext>(o => o.UseSqlite(other.ConnectionString)), createTables: false);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith($"{_hostSetup}TestHostContext and the store contexts on one database", HostFailureOf(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_StoreProviderThatIsNotRelational_FailsOnlyTheCheckOfTheStores()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContext>(o => o.UseSqlite(_database.ConnectionString));
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseEntityFrameworkStores<TestHostContext>(static o => o.UseInMemoryDatabase("stores"));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith($"{_hostSetup}a relational EF Core provider, and {_inMemoryProvider}", HostFailureOf(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseEntityFrameworkStoresOfTheHost_AfterUseEntityFrameworkStores_NamesBothSetupMethods()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddDbContext<TestHostContext>(o => o.UseSqlite(_database.ConnectionString));
        services.AddMsgHatch()
            .UseEntityFrameworkStores(o => o.UseSqlite(_database.ConnectionString))
            .UseEntityFrameworkStores<TestHostContext>(o => o.UseSqlite(_database.ConnectionString));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(
            "UseEntityFrameworkStores and UseEntityFrameworkStores<TestHostContext> select an implementation of IInboxStore.",
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    private static (string?, EntityState)[] TrackedRows(AsyncServiceScope scope) =>
        [.. scope.ServiceProvider.GetRequiredService<TestHostContext>().ChangeTracker.Entries()
            .Select(static entry => (entry.Metadata.GetTableName(), entry.State))];

    private static string HostFailureOf(Exception? exception) =>
        Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures, static failure => failure.StartsWith(_hostSetup, StringComparison.Ordinal));

    private static async Task AcceptAndSaveAsync(ServiceProvider provider, MessageContext context, params TestOrder[] orders)
    {
        await using var scope = provider.CreateAsyncScope();
        var host = scope.ServiceProvider.GetRequiredService<TestHostContext>();
        host.Orders.AddRange(orders);

        await scope.ServiceProvider.GetRequiredService<INotificationService>().AcceptAsync(new TestEvent("text"), context, Ct);
        await host.SaveChangesAsync(Ct);
    }

    /// <summary>
    /// New services of <c>UseEntityFrameworkStores&lt;TestHostContext&gt;</c> on the file of the test, with the registration of
    /// the context of the host, by default <c>AddDbContext</c> on the same file. The store contexts create their tables,
    /// and the context of the host creates only its own table, because the inbox is excluded from its migrations. A
    /// dispatch signal replaces the registration of the setup.
    /// </summary>
    private async Task<ServiceProvider> BuildWithHostContextAsync(
        Action<IServiceCollection>? registerHost = null, Action<EntityFrameworkStoreOptions>? configureOptions = null, bool createTables = true,
        IDispatchSignal? signal = null)
    {
        var services = EntityFrameworkTestHost.CreateServices();
        (registerHost ?? (s => s.AddDbContext<TestHostContext>(o => o.UseSqlite(_database.ConnectionString))))(services);
        services.AddMsgHatch(static o => (o.EnableLogging, o.Roles) = (false, MsgHatchRoles.Consumer))
            .AddTestChannel()
            .UseEntityFrameworkStores<TestHostContext>(o => o.UseSqlite(_database.ConnectionString), configureOptions)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, _) => _router)
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        if (signal is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(signal));
        }

        var provider = services.Build();
        if (createTables)
        {
            await _database.ResetAsync(provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>(), Ct);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TestHostContext>().GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Ct);
        }

        return provider;
    }
}
