using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Data.EFCore.MySql.DependencyInjection;
using MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkMySqlSubscriptionStore</c> with no database (D318, D320): the startup validation, and the model has no pending
/// change against the migrations of the package. No test opens a connection, so the connection string names a closed
/// port.
/// </summary>
public sealed class EntityFrameworkMySqlSubscriptionStoreExtensionsUnitTests
{
    private const string _connectionString = "Server=127.0.0.1;Port=1;Database=setup";

    public static TheoryData<string?, string?> ModelRows => new()
    {
        { null, null },
        { "utf8mb4_0900_as_cs", null },
        { null, "json" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkMySqlSubscriptionStore_Alone_PassesTheStartupValidation()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseEntityFrameworkMySqlSubscriptionStore(_connectionString);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_CollationAndJsonTypeOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string? collation, string? json)
    {
        // Arrange
        await using var provider = MigratedSubscriptionStores.Build(
            _connectionString, o => (o.TextCollation, o.JsonColumnType) = (collation, json));
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchSubscriptionDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }
}
