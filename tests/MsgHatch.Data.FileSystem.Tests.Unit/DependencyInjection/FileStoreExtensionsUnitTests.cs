using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.FileSystem.DependencyInjection;
using MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.FileSystem.Tests.Unit.DependencyInjection;

/// <summary><c>UseFileStores</c> alone registers all parts of the package (section 4.7, D60). Each test has its own temporary folder.</summary>
public sealed class FileStoreExtensionsUnitTests : IDisposable
{
    private readonly string _rootPath = StoreFolders.New();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    public void Dispose() => StoreFolders.Delete(_rootPath);

    [Fact]
    public async Task UseFileStores_Alone_ResolvesEachStoreAsItsLoggingWrapperAroundTheFileStores()
    {
        // Arrange
        await using var provider = Build(enableLogging: true);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        var outgoing = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([outgoing], Ct);

        // Act
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal([1200, 1202, 1300, 1304], provider.LogRecords().Select(static record => record.Id.Id).Order());
        Assert.Equal(
            [(received.Delivery.Id, Path.Combine("inbox", "claimed")), (outgoing.Delivery.Id, Path.Combine("outbox", "Test", "claimed"))],
            [(Assert.Single(inbox.Value.Messages).Delivery.Id, ClaimedFolderOf("inbox")), (Assert.Single(outbox.Value.Messages).Delivery.Id, ClaimedFolderOf("outbox"))]);
    }

    [Fact]
    public async Task UseFileStores_LoggingDisabled_TheDispatchStoresAreTheOneInstanceOfEachFileStore()
    {
        // Arrange
        await using var provider = Build(enableLogging: false);

        // Act
        var inbox = provider.GetRequiredService<IInboxDispatchStore>();
        var outbox = provider.GetRequiredService<IOutboxDispatchStore>();

        // Assert
        Assert.Same(provider.GetRequiredService<FileSystemInboxStore>(), inbox);
        Assert.Same(provider.GetRequiredService<FileSystemOutboxStore>(), outbox);
    }

    [Fact]
    public async Task UseFileStores_RootPathInTheSection_ReachesTheOptionsAndBothStores()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:FileStores:RootPath"] = _rootPath });
        services.AddMsgHatch().UseFileStores();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<FileStoreOptions>>().Value;

        // Assert
        Assert.Equal(
            [_rootPath, _rootPath, _rootPath],
            [options.RootPath, provider.GetRequiredService<FileSystemInboxStore>().RootPath, provider.GetRequiredService<FileSystemOutboxStore>().RootPath]);
    }

    [Fact]
    public async Task UseFileStores_RootPathFromCode_WinsOverTheSection()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:FileStores:RootPath"] = "from-the-section" });
        services.AddMsgHatch().UseFileStores(_rootPath);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<FileStoreOptions>>().Value;

        // Assert
        Assert.Equal(_rootPath, options.RootPath);
    }

    [Fact]
    public async Task UseFileStores_NoRootPath_FailsTheValidationOnRootPath()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseFileStores();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<FileStoreOptions>>().Value);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        var failure = Assert.Single(validation.Failures);
        Assert.Equal("RootPath", failure[..failure.IndexOf(':', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task UseFileStores_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseFileStores select an implementation of ";
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseFileStores(_rootPath);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["IInboxDispatchStore", "IInboxStore", "IOutboxDispatchStore", "IOutboxStore"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Where(static failure => failure.StartsWith(names, StringComparison.Ordinal))
                .Select(static failure => failure[names.Length..^1])
                .Order(StringComparer.Ordinal));
    }

    private string ClaimedFolderOf(string store) =>
        StoreFolders.RelativeFolderOf(_rootPath, Assert.Single(StoreFolders.MessageFiles(_rootPath, store)));

    private ServiceProvider Build(bool enableLogging)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .UseFileStores(_rootPath)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
