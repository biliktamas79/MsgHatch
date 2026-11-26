using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration;

/// <summary>
/// The migration examples of the README of the package run on databases of the fixture. The README check and the
/// other examples of the README are in the unit test project of the package (D311, D330).
/// </summary>
public sealed class ReadmeIntegTests(MySqlFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateMsgHatchAsync_StepOfTheReadme_CreatesTheTablesOfBothContexts()
    {
        // Arrange
        var database = server.Database("readme_migrate", MigratedSubscriptionStores.TablePrefix);
        await using var serviceProvider = MigratedSubscriptionStores.Build(database.ConnectionString);
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion

        // Assert
        Assert.Equal(8, (await MySqlSchema.TablesOfAsync(database, Ct)).Count);
    }
}
