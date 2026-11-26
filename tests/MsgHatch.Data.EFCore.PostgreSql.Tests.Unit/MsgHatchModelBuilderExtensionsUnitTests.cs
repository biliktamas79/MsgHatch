using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit;

/// <summary>
/// <c>MapMsgHatchInbox</c> on the provider of the package (D331): the inbox table of a context of the host equals the inbox
/// table of <see cref="MsgHatchDbContext"/> of the package, with the dialect of the package. The model needs no
/// connection, so the connection string names a closed port.
/// </summary>
public sealed class MsgHatchModelBuilderExtensionsUnitTests
{
    private const string _connectionString = "Host=127.0.0.1;Port=1;Database=host";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MapMsgHatchInbox_HostWithTheDialectOfThePackage_GivesTheInboxTableOfMsgHatchDbContext()
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString);
        await using var store = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        var expected = TableDescriptions.Of(store, "Inbox");
        var options = new DbContextOptionsBuilder<TestHostContext>().UseNpgsql(_connectionString);
        options.IgnoreManyServiceProvidersWarning();
        options.UseEntityFrameworkStoreDialect(PostgreSqlStoreDialect.Instance);
        await using var host = new TestHostContext(options.Options);

        // Act
        var table = TableDescriptions.Of(host, "Inbox");

        // Assert
        Assert.Equal(expected, table);
    }
}
