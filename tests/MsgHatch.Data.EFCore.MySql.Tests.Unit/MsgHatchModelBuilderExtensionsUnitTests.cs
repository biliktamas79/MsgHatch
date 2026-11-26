using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.MySql.Tests.Unit.TestTypes;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using Microting.EntityFrameworkCore.MySql.Infrastructure;

namespace MsgHatch.Data.EFCore.MySql.Tests.Unit;

/// <summary>
/// <c>MapMsgHatchInbox</c> on the provider of the package (D331): the inbox table of a context of the host equals the inbox
/// table of <see cref="MsgHatchDbContext"/> of the package, with the dialect of the package. The model needs no
/// connection, so the connection string names a closed port. The character set of a table or a column goes only into a
/// migration, and the migrations of a host never contain the inbox, so the test leaves it out.
/// </summary>
public sealed class MsgHatchModelBuilderExtensionsUnitTests
{
    private const string _connectionString = "Server=127.0.0.1;Port=1;Database=host";

    private const string _charSet = "MySql:CharSet";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MapMsgHatchInbox_HostWithTheDialectOfThePackage_GivesTheInboxTableOfMsgHatchDbContext()
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString);
        await using var store = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);
        var expected = TableDescriptions.Of(store, "Inbox", _charSet);
        var options = new DbContextOptionsBuilder<TestHostContext>().UseMySql(_connectionString, ServerVersion.Create(8, 4, 0, ServerType.MySql));
        options.IgnoreManyServiceProvidersWarning();
        options.UseEntityFrameworkStoreDialect(MySqlStoreDialect.Instance);
        await using var host = new TestHostContext(options.Options);

        // Act
        var table = TableDescriptions.Of(host, "Inbox", _charSet);

        // Assert
        Assert.Equal(expected, table);
    }
}
