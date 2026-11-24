using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkStores</c> on the EF Core SQLite provider: the stores run on each relational provider, and the
/// startup check refuses a provider that is not relational (D175, D318).
/// </summary>
public sealed class EntityFrameworkStoreExtensionsUnitTests
{
    private const string _inMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

    private readonly SqliteTestDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    [Fact]
    public async Task UseEntityFrameworkStores_Sqlite_PassesTheStartupValidationAndClaimsTheAddedMessages()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString));
        await _database.ResetAsync(provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>(), Ct);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        var outgoing = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([outgoing], Ct);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Null(exception);
        Assert.Equal(
            [received.Delivery.Id, outgoing.Delivery.Id],
            [Assert.Single(inbox.Value.Messages).Delivery.Id, Assert.Single(outbox.Value.Messages).Delivery.Id]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_ProviderThatIsNotRelational_FailsTheStartupValidationAndNamesTheProvider()
    {
        // Arrange
        await using var provider = Build(static o => o.UseInMemoryDatabase("msghatch"));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains("UseEntityFrameworkStores", StringComparison.Ordinal), failure.Contains(_inMemoryProvider, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SqliteWithADialectOfSqlite_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect()));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_DialectOfAnotherProvider_FailsTheStartupValidationAndNamesBothProviders()
    {
        // Arrange
        await using var provider = Build(
            o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect(providerName: "Other.Provider")));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true, true),
            (failure.Contains("UseEntityFrameworkStores", StringComparison.Ordinal), failure.Contains("Other.Provider", StringComparison.Ordinal),
                failure.Contains(SqliteTestDialect.SqliteProvider, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ClaimDueAsync_ProviderThatIsNotRelational_GivesNotFoundForTheInboxAndTheOutbox()
    {
        // Arrange
        var contexts = new PooledDbContextFactory<MsgHatchDbContext>(
            new DbContextOptionsBuilder<MsgHatchDbContext>().UseInMemoryDatabase("msghatch").Options);
        var json = new MsgHatchJson(TestContentTypes.ARegistry());

        // Act
        var inbox = await new EntityFrameworkInboxStore(contexts, json).ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await new EntityFrameworkOutboxStore(contexts, json).ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal([Errors.NotFound, Errors.NotFound], [inbox.Error?.Type, outbox.Error?.Type]);
    }

    private static ServiceProvider Build(Action<DbContextOptionsBuilder> configure)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => (o.EnableLogging, o.Roles) = (false, MsgHatchRoles.Consumer))
            .AddTestChannel()
            .UseEntityFrameworkStores(configure)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
