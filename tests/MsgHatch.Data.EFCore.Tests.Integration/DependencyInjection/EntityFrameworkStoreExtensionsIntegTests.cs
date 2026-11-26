using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkStores</c> alone registers all parts of the package on SQL Server (section 4.7, D60).
/// </summary>
public sealed class EntityFrameworkStoreExtensionsIntegTests(SqlServerFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    private TestDatabase Database => server.Database("setup");

    [Fact]
    public async Task UseEntityFrameworkStores_Alone_ResolvesEachStoreAsItsLoggingWrapperAroundTheEntityFrameworkStores()
    {
        // Arrange
        await Database.ResetAsync(Ct);
        await using var provider = Build(o => o.UseSqlServer(Database.ConnectionString), enableLogging: true);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        var outgoing = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([outgoing], Ct);

        // Act
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(
            [1200, 1202, 1300, 1304],
            provider.LogRecords().Where(static record => record.Category?.StartsWith("MsgHatch.", StringComparison.Ordinal) == true)
                .Select(static record => record.Id.Id).Order());
        Assert.Equal(
            [received.Delivery.Id, outgoing.Delivery.Id],
            [Assert.Single(inbox.Value.Messages).Delivery.Id, Assert.Single(outbox.Value.Messages).Delivery.Id]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaFromCode_KeepsTheTablesAndTheRowsInThatSchema()
    {
        // Arrange
        const string schema = "other";
        var database = server.Database("schema");
        await using var provider = Build(o => o.UseSqlServer(database.ConnectionString), enableLogging: false, o => o.Schema = schema);
        await using (var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct))
        {
            await context.Database.EnsureCreatedAsync(Ct);
        }

        var message = TestMessages.AReceivedMessage(new TestEvent("text"));

        // Act
        var error = await provider.GetRequiredService<IInboxStore>().AddAsync(message, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal(
            [[schema, "Inbox", "1"]],
            await database.RowsAsync(
                $"SELECT TABLE_SCHEMA, TABLE_NAME, (SELECT COUNT(*) FROM [{schema}].[Inbox]) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Inbox'",
                Ct));
    }

    private static ServiceProvider Build(
        Action<DbContextOptionsBuilder> configure, bool enableLogging, Action<EntityFrameworkStoreOptions>? configureOptions = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .UseEntityFrameworkStores(configure, configureOptions)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
