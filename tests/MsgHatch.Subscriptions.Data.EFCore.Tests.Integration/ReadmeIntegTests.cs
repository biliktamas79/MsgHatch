using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Subscriptions.Data.EFCore.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration;

/// <summary>
/// The migration examples of the README of the package run on databases of the fixture. The README check and the
/// other examples of the README are in the unit test project of the package (D311, D330).
/// </summary>
public sealed class ReadmeIntegTests(SqlServerFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateMsgHatchAsync_StepOfTheReadme_CreatesTheSubscriptionTable()
    {
        // Arrange
        var database = server.Database("readme_migrate");
        var services = EntityFrameworkTestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkSqlServerSubscriptionStore(database.ConnectionString);
        await using var serviceProvider = services.Build();
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion

        // Assert
        Assert.Equal(
            [["Subscription"], ["__SubscriptionMigrations"]],
            await database.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME DESC", Ct));
    }
}
