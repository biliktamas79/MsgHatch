using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Resend.DependencyInjection;
using MsgHatch.Email.Resend.Tests.Integration.TestTypes;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Resend.Tests.Integration;

/// <summary>
/// The Resend client against the real Resend API, through <c>UseResend</c> (section 4.6). The account comes from
/// <see cref="ResendAccount"/>, and each test is skipped while its API key is empty. Each accepted email counts toward the
/// quota of the account. A test with a wrong API key sends no email.
/// </summary>
public sealed class ResendEmailClientIntegTests
{
    private const string _wrongApiKey = "re_msghatch_wrong_key";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Skip = ResendAccount.NotConfigured, SkipUnless = nameof(ResendAccount.IsConfigured), SkipType = typeof(ResendAccount))]
    public async Task SendAsync_EmailWithAccentedLetters_ReturnsTheIdOfResend()
    {
        // Arrange
        await using var provider = HostWith();
        var email = new EmailMessage(ResendAccount.Settings.Recipient!, "MsgHatch test: Árvíztűrő tükörfúrógép", "<p>Ödön Űr</p>", "Ödön Űr");

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Value.ProviderMessageId!);
    }

    [Fact(Skip = ResendAccount.NotConfigured, SkipUnless = nameof(ResendAccount.IsConfigured), SkipType = typeof(ResendAccount))]
    public async Task SendAsync_SenderNameWithAccentedLetters_ReturnsTheIdOfResend()
    {
        // Arrange
        await using var provider = HostWith(senderName: "Példa Kft. Árvíztűrő");
        var email = new EmailMessage(ResendAccount.Settings.Recipient!, "MsgHatch test: sender name", "<p>Sender name</p>");

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Value.ProviderMessageId!);
    }

    [Fact(Skip = ResendAccount.NotConfigured, SkipUnless = nameof(ResendAccount.IsConfigured), SkipType = typeof(ResendAccount))]
    public async Task SendAsync_SameMessageIdTwice_ReturnsTheIdOfTheFirstSendAgain()
    {
        // Arrange
        await using var provider = HostWith();
        var client = provider.GetRequiredService<IEmailClient>();
        var id = Guid.NewGuid();
        var email = new EmailMessage(ResendAccount.Settings.Recipient!, "MsgHatch test: idempotency", "<p>Sent one time</p>");
        var first = await client.SendAsync(id, email, Ct);

        // Act
        var second = await client.SendAsync(id, email, Ct);

        // Assert
        Assert.Null(first.Error);
        Assert.Equal(first.Value.ProviderMessageId, second.Value.ProviderMessageId);
    }

    [Fact(Skip = ResendAccount.NotConfigured, SkipUnless = nameof(ResendAccount.IsConfigured), SkipType = typeof(ResendAccount))]
    public async Task SendAsync_WrongApiKey_ReturnsUnavailable()
    {
        // Arrange
        await using var provider = HostWith(apiKey: _wrongApiKey);
        var email = new EmailMessage(ResendAccount.Settings.Recipient!, "MsgHatch test: wrong API key", "<p>Not sent</p>");

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact(Skip = ResendAccount.NotConfigured, SkipUnless = nameof(ResendAccount.IsConfigured), SkipType = typeof(ResendAccount))]
    public async Task CheckHealthAsync_ApiKeyOfTheAccount_ReturnsNull()
    {
        // Arrange
        await using var provider = HostWith();

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact(Skip = ResendAccount.NotConfigured, SkipUnless = nameof(ResendAccount.IsConfigured), SkipType = typeof(ResendAccount))]
    public async Task CheckHealthAsync_WrongApiKey_ReturnsUnavailable()
    {
        // Arrange
        await using var provider = HostWith(apiKey: _wrongApiKey);

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    private static ServiceProvider HostWith(string? senderName = null, string? apiKey = null)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseResend(o =>
        {
            o.ApiKey = apiKey ?? ResendAccount.Settings.ApiKey!;
            o.SenderEmail = ResendAccount.Settings.SenderEmail!;
            o.SenderName = senderName;
        });
        return services.Build();
    }
}
