using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The migration
/// example runs on a database of the fixture.
/// </summary>
public sealed class ReadmeIntegTests(PostgreSqlFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlSubscriptionStore_SetupOfTheReadme_GivesTheContextTheProviderOfPostgreSql()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = server.Database("readme").ConnectionString;

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEntityFrameworkPostgreSqlStores(connectionString)
            .UseSubscriptions()
            .UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString);
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public async Task MigrateMsgHatchAsync_StepOfTheReadme_CreatesTheTablesOfBothContexts()
    {
        // Arrange
        var database = server.Database("readme_migrate");
        await using var serviceProvider = MigratedSubscriptionStores.Build(database.ConnectionString);
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion

        // Assert
        Assert.Equal(8, (await PostgreSqlSchema.TablesOfAsync(database, Ct)).Count);
    }
}
