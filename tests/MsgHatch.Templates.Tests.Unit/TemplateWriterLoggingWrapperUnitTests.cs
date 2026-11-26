using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit;

/// <summary>
/// The standard logging wrapper tests for <see cref="ITemplateWriter"/> (D218): the wrapper of a writable store is a
/// writer too, and the wrapper of a read-only store is not.
/// </summary>
public sealed class TemplateWriterLoggingWrapperUnitTests
{
    private const string _text = "Order for client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly TemplateLookup _lookup = new(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText);
    private static readonly TemplateEntry _entry = new(_lookup, 638_000_000_000_000_001);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UseTemplateStore_WritableStore_ResolvesAsAWriterThatGivesTheResultOfTheInnerWriter(bool enableLogging)
    {
        // Arrange
        var inner = AWritableStore();
        await using var provider = BuildWithSharedStore(inner, enableLogging);

        // Act
        var result = await ((ITemplateWriter)provider.GetRequiredService<ITemplateStore>()).SaveAsync(_lookup, _text, 0, Ct);

        // Assert
        Assert.Same(_entry, result.Value);
        await ((ITemplateWriter)inner).Received(1).SaveAsync(_lookup, _text, 0, Ct);
    }

    [Fact]
    public async Task UseTemplateStore_ReadOnlyStore_ResolvesAsNoWriter()
    {
        // Arrange
        await using var provider = BuildWithSharedStore(Substitute.For<ITemplateStore>());

        // Act
        var store = provider.GetRequiredService<ITemplateStore>();

        // Assert
        Assert.False(store is ITemplateWriter);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UseFileTemplates_LoggingOnOrOff_ResolvesTheSharedStoreAsAWriter(bool enableLogging)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseFileTemplates(Path.Combine(Path.GetTempPath(), "msghatch-tests", "writer"));
        await using var provider = services.Build();

        // Act
        var store = provider.GetRequiredService<ITemplateStore>();

        // Assert
        Assert.True(store is ITemplateWriter);
    }

    [Fact]
    public async Task UseEmbeddedTemplates_SharedStore_ResolvesAsNoWriter()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEmbeddedTemplates(Assembly.GetExecutingAssembly(), "MsgHatch.Templates.Tests.Unit.TemplateFiles");
        await using var provider = services.Build();

        // Act
        var store = provider.GetRequiredService<ITemplateStore>();

        // Assert
        Assert.False(store is ITemplateWriter);
    }

    [Fact]
    public async Task UseTenantTemplateStore_FileStoreOfATenant_ResolvesAsAWriter()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseTenantTemplateStore(static (_, key) =>
            new FileSystemTemplateStore(Path.Combine(Path.GetTempPath(), "msghatch-tests", key.Tenant!.Value.Value)));
        await using var provider = services.Build();

        // Act
        var store = provider.GetRequiredKeyedService<ITemplateStore>(_t1);

        // Assert
        Assert.True(store is ITemplateWriter);
    }

    [Fact]
    public async Task SaveAsync_Entry_LogsInformationWithItsEventIdAndTheVersionAndNotTheText()
    {
        // Arrange
        var inner = AWritableStore();
        await using var provider = BuildWithSharedStore(inner);

        // Act
        await Writer(provider).SaveAsync(_lookup, _text, 0, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(
            (LogLevel.Information, 2010, _entry.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("Version"), record.ContainsText(_text)));
        await ((ITemplateWriter)inner).Received(1).SaveAsync(_lookup, _text, 0, Ct);
    }

    [Fact]
    public async Task SaveAsync_Conflict_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var inner = AWritableStore();
        ((ITemplateWriter)inner).SaveAsync(default, default!, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<TemplateEntry>>(new Error(Errors.Conflict)));
        await using var provider = BuildWithSharedStore(inner);

        // Act
        await Writer(provider).SaveAsync(_lookup, _text, 0, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2011, false), (record.Level, record.Id.Id, record.ContainsText(_text)));
        await ((ITemplateWriter)inner).Received(1).SaveAsync(_lookup, _text, 0, Ct);
    }

    [Fact]
    public async Task DeleteAsync_NoError_LogsInformationWithItsEventId()
    {
        // Arrange
        var inner = AWritableStore();
        await using var provider = BuildWithSharedStore(inner);

        // Act
        var error = await Writer(provider).DeleteAsync(_lookup, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((null, LogLevel.Information, 2012), (error, record.Level, record.Id.Id));
        await ((ITemplateWriter)inner).Received(1).DeleteAsync(_lookup, Ct);
    }

    [Fact]
    public async Task DeleteAsync_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var inner = AWritableStore();
        ((ITemplateWriter)inner).DeleteAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        await using var provider = BuildWithSharedStore(inner);

        // Act
        var result = await Writer(provider).DeleteAsync(_lookup, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((error, LogLevel.Warning, 2013), (result, record.Level, record.Id.Id));
        await ((ITemplateWriter)inner).Received(1).DeleteAsync(_lookup, Ct);
    }

    [Fact]
    public async Task ListAsync_Entries_ReturnsThemAndLogsDebugWithTheCount()
    {
        // Arrange
        var inner = AWritableStore();
        await using var provider = BuildWithSharedStore(inner);

        // Act
        var entries = await Writer(provider).ListAsync(new TemplateQuery(), Ct);

        // Assert
        Assert.Equal([_entry, _entry], entries.Value);
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 2014, "2"), (record.Level, record.Id.Id, record.GetStructuredStateValue("Count")));
        await ((ITemplateWriter)inner).Received(1).ListAsync(new TemplateQuery(), Ct);
    }

    [Fact]
    public async Task ListAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = AWritableStore();
        ((ITemplateWriter)inner).ListAsync(default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<TemplateEntry>>>(new Error(Errors.Unavailable)));
        await using var provider = BuildWithSharedStore(inner);

        // Act
        await Writer(provider).ListAsync(new TemplateQuery(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2015), (record.Level, record.Id.Id));
        await ((ITemplateWriter)inner).Received(1).ListAsync(new TemplateQuery(), Ct);
    }

    [Fact]
    public async Task SaveAsync_TenantTemplateStoreOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        var inner = AWritableStore();
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseTenantTemplateStore((_, _) => inner);
        await using var provider = services.Build();

        // Act
        await ((ITemplateWriter)provider.GetRequiredKeyedService<ITemplateStore>(_t1)).SaveAsync(_lookup, _text, 0, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", 2010), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
        await ((ITemplateWriter)inner).Received(1).SaveAsync(_lookup, _text, 0, Ct);
    }

    [Fact]
    public async Task RenderVersion_WritableStore_GivesTheTextOfTheInnerStore()
    {
        // Arrange
        const string versionText = "Version of the entry";
        var inner = AWritableStore();
        inner.RenderVersion(_entry.Version).Returns(versionText);
        await using var provider = BuildWithSharedStore(inner);

        // Act
        var text = provider.GetRequiredService<ITemplateStore>().RenderVersion(_entry.Version);

        // Assert
        Assert.Equal((versionText, true), (text, provider.GetRequiredService<ITemplateStore>() is ITemplateWriter));
    }

    private static ITemplateStore AWritableStore()
    {
        var store = Substitute.For<ITemplateStore, ITemplateWriter>();
        var writer = (ITemplateWriter)store;
        writer.SaveAsync(default, default!, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateEntry>.ForValue(_entry)));
        writer.DeleteAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        writer.ListAsync(default!, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForValue([_entry, _entry])));
        return store;
    }

    private static ITemplateWriter Writer(IServiceProvider provider) => (ITemplateWriter)provider.GetRequiredService<ITemplateStore>();

    private static ServiceProvider BuildWithSharedStore(ITemplateStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseTemplateStore(_ => inner);
        return services.Build();
    }
}
