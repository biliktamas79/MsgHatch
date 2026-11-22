using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.Sqlite.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() =>
        ReadmeCheck.AssertOptionsTables(typeof(SqliteStoreOptions), typeof(SqliteTenantStoreOptions));

    [Fact]
    public async Task UseSqliteStores_SettingsOfTheReadme_ResolvesTheOutboxStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseSqliteStores();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IOutboxStore>());
        Assert.Equal("Data Source=msghatch.db", provider.GetRequiredService<IOptions<SqliteStoreOptions>>().Value.ConnectionString);
    }

    [Fact]
    public async Task UseSqliteStores_ConnectionStringInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseSqliteStores("Data Source=msghatch.db");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("Data Source=msghatch.db", provider.GetRequiredService<IOptions<SqliteStoreOptions>>().Value.ConnectionString);
    }

    [Fact]
    public async Task UseSqliteTenantStores_SettingsOfTheReadme_PassTheValidation()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("tenant-options.json"));

        // Act
        #region readme:tenants
        services.AddMsgHatch()
            .UseSqliteTenantStores<ShopTenants>();
        #endregion
        await using var provider = services.Build();

        // Assert
        var options = provider.GetRequiredService<IOptions<SqliteTenantStoreOptions>>().Value;
        Assert.Equal("Data Source=data/msghatch-{tenant}.db", options.TenantConnectionString);
        Assert.Equal("Data Source=data/msghatch.db", options.SystemConnectionString);
    }

    private sealed class ShopTenants : ITenantSource
    {
        public ValueTask<ErrorOr<IReadOnlyList<TenantId>>> GetTenantsAsync(CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TenantId>>.ForValue([]));
    }
}
