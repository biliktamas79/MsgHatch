using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkStores</c> on the EF Core SQL Server provider: the registrations, the options, and the setup check
/// (section 4.7, D60). No test opens a connection, so the connection string names a closed port.
/// </summary>
public sealed partial class EntityFrameworkStoreExtensionsUnitTests
{
    private const string _sqlServerConnectionString = "Server=127.0.0.1,1;Database=setup";

    private const string _schemaKey = "MsgHatch:EntityFramework:Schema";

    [Fact]
    public async Task UseEntityFrameworkStores_LoggingDisabled_TheStoresAreTheOneInstanceOfEachEntityFrameworkStore()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlServer(_sqlServerConnectionString), enableLogging: false);

        // Act
        object[] stores =
        [
            provider.GetRequiredService<IInboxDispatchStore>(), provider.GetRequiredService<IOutboxDispatchStore>(),
            provider.GetRequiredService<IRegistrySettingsStore>(),
        ];

        // Assert
        Assert.Equal(
            [provider.GetRequiredService<EntityFrameworkInboxStore>(), provider.GetRequiredService<EntityFrameworkOutboxStore>(),
                provider.GetRequiredService<EntityFrameworkRegistrySettingsStore>()],
            stores);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaInTheSection_ReachesTheOptions()
    {
        // Arrange
        const string schema = "from_section";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_schemaKey] = schema });
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlServer(_sqlServerConnectionString));
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value;

        // Assert
        Assert.Equal(schema, options.Schema);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaFromCode_WinsOverTheSection()
    {
        // Arrange
        const string schema = "from_code";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_schemaKey] = "from_section" });
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlServer(_sqlServerConnectionString), o => o.Schema = schema);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value;

        // Assert
        Assert.Equal(schema, options.Schema);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SchemaThatIsNotAnIdentifier_FailsTheValidationOnSchema()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_schemaKey] = "msg-hatch" });
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlServer(_sqlServerConnectionString));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal("Schema", failure[..failure.IndexOf(':', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseEntityFrameworkStores select an implementation of ";
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseEntityFrameworkStores(o => o.UseSqlServer(_sqlServerConnectionString));
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

    private static ServiceProvider Build(Action<DbContextOptionsBuilder> configure, bool enableLogging)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .UseEntityFrameworkStores(configure)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
