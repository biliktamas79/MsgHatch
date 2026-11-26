using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The check reads the
/// sources of this project and of the integration test project of the package, where the migration examples run on a
/// server (D330). No test of this class opens a connection.
/// </summary>
public sealed class ReadmeUnitTests
{
    private const string _connectionString = "Server=127.0.0.1,1;Database=readme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task AddExceptionClassifier_ClassifierOfTheReadme_IsTheClassifierOfTheStores()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:classifier
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(connectionString)
            .AddExceptionClassifier<MsgHatchDbContext, SqlServerReadOnlyClassifier>();
        #endregion
        await using var provider = services.Build();

        // Assert
        var classifier = Assert.Single(provider.GetServices<IExceptionClassifier<MsgHatchDbContext>>());
        Assert.Null(classifier.Classify(new DbUpdateException("update", new InvalidOperationException())));
    }

    [Fact]
    public async Task UseEntityFrameworkSqlServerStores_SettingsOfTheReadme_RequiresTheCurrentSchema()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = _connectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.RequireCurrentSchema);
    }

    [Fact]
    public async Task UseEntityFrameworkSqlServerStores_OptionsInCode_UsesTheSchemaAndTheRetriesOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:setup-options
        services.AddMsgHatch()
            .UseEntityFrameworkSqlServerStores(
                connectionString,
                o => o.Schema = "messaging",
                sql => sql.EnableRetryOnFailure());
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(("messaging", true), (context.Model.GetDefaultSchema(), context.Database.CreateExecutionStrategy().RetriesOnFailure));
    }
}
