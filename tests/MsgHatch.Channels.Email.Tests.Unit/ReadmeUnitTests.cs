using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Channels.Email.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Channels.Email.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseEmailClient_ClientOfTheHost_ResolvesTheEmailClient()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseEmailClient<SmtpEmailClient>();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.IsType<SmtpEmailClient>(provider.GetRequiredService<IEmailClient>());
    }

    private sealed class SmtpEmailClient : IEmailClient
    {
        public ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<PublishReceipt>.ForValue(new PublishReceipt(messageId.ToString("N"))));

        public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);
    }
}
