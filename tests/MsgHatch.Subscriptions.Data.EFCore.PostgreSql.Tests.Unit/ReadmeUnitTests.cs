using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Unit;

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
    public async Task UseEntityFrameworkPostgreSqlSubscriptionStore_SetupOfTheReadme_GivesTheContextTheProviderOfPostgreSql()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var connectionString = _connectionString;

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
}
