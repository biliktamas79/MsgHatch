using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Channels.Broker.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Channels.Broker.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseBrokerClient_ClientOfTheHost_ResolvesTheBrokerClient()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseBrokerClient<KafkaBrokerClient>();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IBrokerClient>());
    }

    private sealed class KafkaBrokerClient : IBrokerClient
    {
        public ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<PublishReceipt>.ForValue(new PublishReceipt(ProviderMessageId: null)));

        public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);
    }
}
