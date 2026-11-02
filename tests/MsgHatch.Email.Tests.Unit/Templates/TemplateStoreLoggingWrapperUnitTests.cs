using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.Templates;

/// <summary>The standard logging wrapper tests for <see cref="ITemplateStore"/>, as the shared and as the tenant template store.</summary>
public sealed class TemplateStoreLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly TemplateLookup _lookup = new(new MessageType("TestEvent"), "en", Scope: null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindAsync_InnerSource_ReturnsItUnchanged()
    {
        // Arrange
        var source = ASource("Order shipped");
        await using var provider = BuildWithSharedStore(AStore(ErrorOr<TemplateSource?>.ForValue(source)));

        // Act
        var result = await provider.GetRequiredService<ITemplateStore>().FindAsync(_lookup, Ct);

        // Assert
        Assert.Same(source, result.Value);
    }

    [Fact]
    public async Task FindAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        await using var provider = BuildWithSharedStore(AStore(error));

        // Act
        var result = await provider.GetRequiredService<ITemplateStore>().FindAsync(_lookup, Ct);

        // Assert
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task FindAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWithSharedStore(AStore(new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<ITemplateStore>().FindAsync(_lookup, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2001), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task FindAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWithSharedStore(AStore(new Error(Errors.SerializationFailed)));

        // Act
        await provider.GetRequiredService<ITemplateStore>().FindAsync(_lookup, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2001), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task FindAsync_TenantTemplateStoreOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        var inner = AStore(ErrorOr<TemplateSource?>.ForValue(null));
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseTenantTemplateStore((_, _) => inner);
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredKeyedService<ITemplateStore>(_t1).FindAsync(_lookup, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", 2000), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
    }

    [Fact]
    public async Task FindAsync_TemplateWithAnAddressInItsText_NoLogRecordContainsTheText()
    {
        // Arrange
        await using var provider = BuildWithSharedStore(AStore(ErrorOr<TemplateSource?>.ForValue(ASource($"Order for {_address}"))));

        // Act
        await provider.GetRequiredService<ITemplateStore>().FindAsync(_lookup, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
    }

    [Fact]
    public async Task UseTemplateStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(ErrorOr<TemplateSource?>.ForValue(null));
        await using var provider = BuildWithSharedStore(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredService<ITemplateStore>();

        // Assert
        Assert.Same(inner, store);
    }

    private static TemplateSource ASource(string subject) =>
        PlaceholderTemplateTestData.ASource([new TemplatePart("subject", TemplatePartKind.PlainText, subject)]);

    private static ITemplateStore AStore(ErrorOr<TemplateSource?> result)
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return store;
    }

    private static ServiceProvider BuildWithSharedStore(ITemplateStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseTemplateStore(_ => inner);
        return services.Build();
    }
}
