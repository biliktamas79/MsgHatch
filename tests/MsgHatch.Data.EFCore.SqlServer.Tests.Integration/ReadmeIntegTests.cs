using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.DependencyInjection;
using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>
/// The migration examples of the README of the package run on databases of the fixture. The README check and the
/// other examples of the README are in the unit test project of the package (D311, D330).
/// </summary>
public sealed class ReadmeIntegTests(SqlServerFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrateMsgHatchAsync_StepsOfTheReadme_MigrateTheDatabaseOfEachStepAndScriptTheMigrations()
    {
        // Arrange
        var database = server.Database("readme_migrate");
        var migrationDatabase = server.Database("readme_migrate_own");
        var migrationConnectionString = migrationDatabase.ConnectionString;
        await using var serviceProvider = MigratedStores.Build(database.ConnectionString);
        var cancellationToken = Ct;

        // Act
        #region readme:migrate
        await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
        #endregion
        #region readme:migrate-own-setup
        await serviceProvider.MigrateMsgHatchAsync(o => o.UseSqlServer(migrationConnectionString), cancellationToken);
        #endregion
        #region readme:script
        var script = await serviceProvider.ScriptMsgHatchMigrationsAsync(cancellationToken);
        #endregion

        // Assert
        Assert.Equal(
            (6, 6, true),
            ((await database.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count,
                (await migrationDatabase.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count,
                script.Contains("__StoreMigrations", StringComparison.Ordinal)));
    }
}
