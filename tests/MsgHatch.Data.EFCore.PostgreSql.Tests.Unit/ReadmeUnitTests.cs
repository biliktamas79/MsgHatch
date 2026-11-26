using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The check reads the
/// sources of this project and of the integration test project of the package, where the migration examples run on a
/// server (D330). No test of this class opens a connection.
/// </summary>
public sealed class ReadmeUnitTests
{
    private const string _connectionString = "Host=127.0.0.1;Port=1;Database=readme";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task AddExceptionClassifier_ClassifierOfTheReadme_RejectsAReadOnlyTransaction()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:classifier
        services.AddMsgHatch()
            .UseEntityFrameworkPostgreSqlStores(connectionString)
            .AddExceptionClassifier<MsgHatchDbContext, PostgreSqlReadOnlyClassifier>();
        #endregion
        await using var provider = services.Build();

        // Assert
        var classifier = Assert.Single(provider.GetServices<IExceptionClassifier<MsgHatchDbContext>>());
        Assert.Equal(
            (Errors.Rejected, (ErrorType?)null),
            (classifier.Classify(new DbUpdateException("update", new PostgresException("read-only", "ERROR", "ERROR", "25006"))),
                classifier.Classify(new PostgresException("serialization", "ERROR", "ERROR", "40001"))));
    }

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlStores_SettingsOfTheReadme_RequiresTheCurrentSchema()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        var connectionString = _connectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkPostgreSqlStores(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value.RequireCurrentSchema);
    }

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlStores_OptionsInCode_UsesTheSchemaAndTheRetriesOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _connectionString;

        // Act
        #region readme:setup-options
        services.AddMsgHatch()
            .UseEntityFrameworkPostgreSqlStores(
                connectionString,
                o => o.Schema = "messaging",
                npgsql => npgsql.EnableRetryOnFailure());
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal(("messaging", true), (context.Model.GetDefaultSchema(), context.Database.CreateExecutionStrategy().RetriesOnFailure));
    }
}
