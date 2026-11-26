using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using Npgsql;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkPostgreSqlStores</c> with no database (D318, D320, D322): the startup validation, the model has no
/// pending change against the migrations of the package, the startup check of <c>RequireCurrentSchema</c> with a doubled
/// history of the migrations, and the error of a missing table with a doubled connection. No test opens a connection, so
/// the connection string names a closed port.
/// </summary>
public sealed class EntityFrameworkPostgreSqlStoreExtensionsUnitTests
{
    private const string _connectionString = "Host=127.0.0.1;Port=1;Database=setup";

    public static TheoryData<string, string?> ModelRows => new()
    {
        { "msghatch", null },
        { "other", "POSIX" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlStores_Alone_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_SchemaAndCollationOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string schema, string? collation)
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString, o => (o.Schema, o.TextCollation) = (schema, collation));
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }

    [Fact]
    public async Task RequireCurrentSchema_MigrationThatIsNotApplied_FailsTheStartupWithTheContextAndTheMigration()
    {
        // Arrange
        await using var provider = MigratedStores.BuildWithDoubles(
            _connectionString, static o => o.ReplaceService<IHistoryRepository, NoAppliedMigrations>(), static o => o.RequireCurrentSchema = true);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains(nameof(MsgHatchDbContext), StringComparison.Ordinal), failure.Contains("_Initial", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Stores_DatabaseWithNoMigrations_GiveValidationFailed()
    {
        // Arrange
        const string undefinedTable = "42P01";
        var missingTable = new FailingConnectionInterceptor(new PostgresException("The message of the server.", "ERROR", "ERROR", undefinedTable));
        await using var provider = MigratedStores.BuildWithDoubles(_connectionString, o => o.AddInterceptors(missingTable));
        var contexts = provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>();
        var json = new MsgHatchJson(TestContentTypes.ARegistry());
        var now = TestHost.Start.UtcDateTime;

        // Act
        var inbox = await new EntityFrameworkInboxStore(contexts, json).ClaimDueAsync(10, now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await new EntityFrameworkOutboxStore(contexts, json).ClaimDueAsync(TestChannel.Id, 10, now, TimeSpan.FromSeconds(30), Ct);
        var registry = await new EntityFrameworkRegistrySettingsStore(contexts).GetEntriesAsync(Ct);

        // Assert
        Assert.Equal(
            (Errors.ValidationFailed, Errors.ValidationFailed, Errors.ValidationFailed, 3),
            (inbox.Error?.Type, outbox.Error?.Type, registry.Error?.Type, missingTable.Opens));
    }
}
