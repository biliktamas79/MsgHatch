using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.Sqlite.Tests.Unit.DependencyInjection;

/// <summary><c>UseSqliteTenantStores</c> keeps one database file for each tenant and one system file (D125, D172). Each test has its own folder.</summary>
public sealed partial class SqliteStoreExtensionsUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static readonly TenantKey _t2 = TenantKey.For(new TenantId("t2"));

    private readonly TenantDatabaseFolder _folder = new();

    [Fact]
    public async Task UseSqliteTenantStores_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:SqliteTenants:TenantConnectionString"] = "Data Source=root-{tenant}.db",
            ["Messaging:SqliteTenants:SystemConnectionString"] = "Data Source=root.db",
            ["MsgHatch:SqliteTenants:SystemConnectionString"] = "Data Source=default.db",
        });
        services.AddSingleton(new FixedTenants([]));
        services.AddMsgHatch(configSectionPath: "Messaging").UseSqliteTenantStores<FixedTenantSource>();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SqliteTenantStoreOptions>>().Value;

        // Assert
        Assert.Equal(("Data Source=root-{tenant}.db", "Data Source=root.db"), (options.TenantConnectionString, options.SystemConnectionString));
    }

    [Fact]
    public async Task UseSqliteTenantStores_TwoTenantsOfTheTenantSource_MakesTheFileOfEachTenantAndTheSystemFile()
    {
        // Arrange
        await using var provider = BuildForEachTenant("t1", "t2");

        // Act
        await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(["msghatch-t1.db", "msghatch-t2.db", "msghatch.db"], _folder.FileNames);
    }

    [Fact]
    public async Task UseSqliteTenantStores_MessagesOfTwoTenantsAndOfNoTenant_KeepsEachMessageInTheFileOfItsKey()
    {
        // Arrange
        await using var provider = BuildForEachTenant("t1", "t2");
        var received = TestMessages.AReceivedMessage(new TestEvent("text"), _t1.Tenant);
        var ofT1 = TestMessages.AnOutgoingMessage(tenant: _t1.Tenant, channel: TestChannel.Id);
        var ofT2 = TestMessages.AnOutgoingMessage(tenant: _t2.Tenant, channel: TestChannel.Id);
        var ofNoTenant = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);

        // Act
        await provider.GetRequiredKeyedService<IInboxStore>(_t1).AddAsync(received, Ct);
        await provider.GetRequiredKeyedService<IOutboxStore>(_t1).AddAsync([ofT1], Ct);
        await provider.GetRequiredKeyedService<IOutboxStore>(_t2).AddAsync([ofT2], Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([ofNoTenant], Ct);

        // Assert
        Assert.Equal(
            [
                [received.Delivery.Id.ToString("D"), ofT1.Delivery.Id.ToString("D")],
                [ofT2.Delivery.Id.ToString("D")],
                [ofNoTenant.Delivery.Id.ToString("D")],
            ],
            [IdsIn("msghatch-t1.db"), IdsIn("msghatch-t2.db"), IdsIn("msghatch.db")]);
    }

    [Fact]
    public async Task UseSqliteTenantStores_DownStateAndRegistryEntry_WritesBothOnlyIntoTheSystemFile()
    {
        // Arrange
        await using var provider = BuildForEachTenant("t1");
        await provider.GetRequiredKeyedService<IOutboxStore>(_t1).AddAsync([TestMessages.AnOutgoingMessage(tenant: _t1.Tenant, channel: TestChannel.Id)], Ct);
        var down = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");

        // Act
        await provider.GetRequiredService<IOutboxDispatchStore>().SetChannelDownAsync(down, Ct);
        await provider.GetRequiredService<IRegistrySettingsStore>().SaveChannelEntryAsync(
            new ChannelEntry(TestChannel.Id, "Shop email", Description: null, Enabled: true, Revision: 1, Now), Ct);

        // Assert
        Assert.Equal(
            [["0", "0"], ["1", "1"]],
            [CountsOfDownAndEntryRows("msghatch-t1.db"), CountsOfDownAndEntryRows("msghatch.db")]);
    }

    [Fact]
    public async Task UseSqliteTenantStores_TenantIdsWithABarAndWithCapitals_GiveEscapedFileNamesThatDifferInCase()
    {
        // Arrange
        await using var provider = BuildForEachTenant("a|b", "Acme", "acme");

        // Act
        await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(["msghatch-^acme.db", "msghatch-a%7^cb.db", "msghatch-acme.db", "msghatch.db"], _folder.FileNames);
    }

    [Fact]
    public async Task UseSqliteTenantStores_NoConnectionStrings_FailsTheValidationOnBothProperties()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(new FixedTenants([]));
        services.AddMsgHatch().UseSqliteTenantStores<FixedTenantSource>();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<SqliteTenantStoreOptions>>().Value);

        // Assert
        Assert.Equal(
            ["SystemConnectionString", "TenantConnectionString"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Select(static failure => failure[..failure.IndexOf(':', StringComparison.Ordinal)])
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UseSqliteTenantStores_ValuesFromCode_WinOverTheSection()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:SqliteTenants:TenantConnectionString"] = "Data Source=from-the-section-{tenant}.db",
            ["MsgHatch:SqliteTenants:SystemConnectionString"] = "Data Source=from-the-section.db",
        });
        services.AddSingleton(new FixedTenants([]));
        services.AddMsgHatch().UseSqliteTenantStores<FixedTenantSource>(o =>
        {
            o.TenantConnectionString = _folder.Section["MsgHatch:SqliteTenants:TenantConnectionString"]!;
            o.SystemConnectionString = _folder.Section["MsgHatch:SqliteTenants:SystemConnectionString"]!;
        });
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SqliteTenantStoreOptions>>().Value;

        // Assert
        Assert.Equal(
            (_folder.ConnectionStringOf("msghatch-{tenant}.db"), _folder.ConnectionStringOf("msghatch.db")),
            (options.TenantConnectionString, options.SystemConnectionString));
    }

    [Fact]
    public async Task UseSqliteTenantStores_AfterUseSqliteStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseSqliteStores and UseSqliteTenantStores select an implementation of ";
        var services = TestHost.CreateServices(configuration: _folder.Section);
        services.AddSingleton(new FixedTenants([]));
        services.AddMsgHatch().UseSqliteStores(ConnectionString).UseSqliteTenantStores<FixedTenantSource>();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["IInboxDispatchStore", "IInboxStore", "IOutboxDispatchStore", "IOutboxStore", "IRegistrySettingsStore"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Where(static failure => failure.StartsWith(names, StringComparison.Ordinal))
                .Select(static failure => failure[names.Length..^1])
                .Order(StringComparer.Ordinal));
    }

    private string?[] IdsIn(string fileName) =>
        [.. TestDatabases.Rows(_folder.ConnectionStringOf(fileName), "SELECT Id FROM MsgHatchInbox UNION ALL SELECT Id FROM MsgHatchOutbox").Select(static row => row[0])];

    private string?[] CountsOfDownAndEntryRows(string fileName) =>
        Assert.Single(TestDatabases.Rows(
            _folder.ConnectionStringOf(fileName),
            "SELECT (SELECT COUNT(*) FROM MsgHatchChannelState), (SELECT COUNT(*) FROM MsgHatchChannelEntry)"));

    private ServiceProvider BuildForEachTenant(params string[] tenants)
    {
        var services = TestHost.CreateServices(configuration: _folder.Section);
        services.AddSingleton(new FixedTenants([.. tenants.Select(static tenant => new TenantId(tenant))]));
        services.AddMsgHatch()
            .AddTestChannel()
            .UseSqliteTenantStores<FixedTenantSource>()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
