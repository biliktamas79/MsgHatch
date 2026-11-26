using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.Tests.Unit.TestTypes;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using NSubstitute;

namespace MsgHatch.Data.EFCore.MySql.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The check reads the
/// sources of this project and of the integration test project of the package, where the migration examples run on a
/// server (D330). No test of this class opens a connection.
/// </summary>
public sealed class ReadmeUnitTests
{
    private const string _connectionString = "Server=127.0.0.1;Port=1;Database=readme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task AddExceptionClassifier_ClassifierOfTheReadme_IsRegisteredForTheStores()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:classifier
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(connectionString)
            .AddExceptionClassifier<MsgHatchDbContext, MySqlReadOnlyClassifier>();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.IsType<MySqlReadOnlyClassifier>(Assert.Single(provider.GetServices<IExceptionClassifier<MsgHatchDbContext>>()));
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_SettingsOfTheReadme_RequiresTheCurrentSchema()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = _connectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.RequireCurrentSchema);
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_OptionsInCode_UsesTheJsonTypeOfTheCode()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:setup-options
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores(
                connectionString,
                o => o.JsonColumnType = "json");
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("json", context.Model.GetEntityTypes().Single(static type => type.GetTableName() == "MsgHatchInbox").FindProperty("Content")!.GetColumnType());
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_ContextOfTheHostOfTheReadme_StagesTheInboxRowInTheContextOfTheHost()
    {
        // Arrange
        var services = EntityFrameworkTestHost.CreateServices();
        var connectionString = _connectionString;
        #region readme:host
        services.AddDbContext<TestHostContext>(o => o.UseMySql(connectionString, ServerVersion.Create(8, 4, 0, ServerType.MySql)));
        services.AddMsgHatch()
            .UseEntityFrameworkMySqlStores<TestHostContext>(connectionString);
        #endregion
        services.AddMsgHatch().AddMessage(
            "TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m.WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>()));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var message = TestMessages.AReceivedMessage(new TestEvent("text"));

        // Act
        var error = await scope.ServiceProvider.GetRequiredService<IInboxStore>().AddAsync(message, Ct);

        // Assert
        Assert.Null(error);
        Assert.Equal([EntityState.Added], scope.ServiceProvider.GetRequiredService<TestHostContext>().ChangeTracker.Entries().Select(static entry => entry.State));
    }
}
