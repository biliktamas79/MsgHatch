using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.AmazonSES.DependencyInjection;
using MsgHatch.Email.AmazonSES.Tests.Integration.TestTypes;
using MsgHatch.Email.AmazonSES.Tests.Unit.TestTypes;
using MsgHatch.Email.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(NoAwsCredentials))]

namespace MsgHatch.Email.AmazonSES.Tests.Integration;

/// <summary>
/// The Amazon SES client against a real SES account, through <c>UseAmazonSes</c> (section 4.6). The account comes from
/// <see cref="SesAccount"/>, and each test is skipped while its settings are not complete. The tests also check the SES
/// address rules of the client: the UTF-8 text, the RFC 2047 sender name, and the refusal of an address outside ASCII.
/// </summary>
public sealed class AmazonSimpleEmailServiceClientIntegTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Skip = SesAccount.NotConfigured, SkipUnless = nameof(SesAccount.IsConfigured), SkipType = typeof(SesAccount))]
    public async Task SendAsync_EmailWithAccentedLetters_ReturnsTheMessageIdOfSes()
    {
        // Arrange
        await using var provider = HostWith(senderName: "MsgHatch");
        var email = new EmailMessage(SesAccount.Settings.Recipient!, "MsgHatch test: Árvíztűrő tükörfúrógép", "<p>Ödön Űr</p>", "Ödön Űr");

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Value.ProviderMessageId!);
    }

    [Fact(Skip = SesAccount.NotConfigured, SkipUnless = nameof(SesAccount.IsConfigured), SkipType = typeof(SesAccount))]
    public async Task SendAsync_SenderNameWithAccentedLetters_ReturnsTheMessageIdOfSes()
    {
        // Arrange
        await using var provider = HostWith(senderName: "Példa Kft. Árvíztűrő");
        var email = new EmailMessage(SesAccount.Settings.Recipient!, "MsgHatch test: sender name", "<p>Sender name</p>");

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Null(result.Error);
        Assert.NotEmpty(result.Value.ProviderMessageId!);
    }

    [Fact(Skip = SesAccount.NotConfigured, SkipUnless = nameof(SesAccount.IsConfigured), SkipType = typeof(SesAccount))]
    public async Task SendAsync_SenderWithAnAccentedLocalPart_ReturnsRejected()
    {
        // Arrange
        await using var provider = HostWith();
        var email = new EmailMessage(SesAccount.Settings.Recipient!, "MsgHatch test: sender outside ASCII", "<p>Refused</p>", From: $"példa@{SesAccount.SenderDomain}");

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.Equal(Errors.Rejected, result.Error?.Type);
    }

    [Fact(Skip = SesAccount.NotConfigured, SkipUnless = nameof(SesAccount.IsConfigured), SkipType = typeof(SesAccount))]
    public async Task CheckHealthAsync_SesAccountThatCanSend_ReturnsNull()
    {
        // Arrange
        await using var provider = HostWith();

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    private static ServiceProvider HostWith(string? senderName = null)
    {
        var services = TestHost.CreateServices();
        services.AddSingleton(static _ => SesAccount.NewSesClient());
        services.AddMsgHatch().UseAmazonSes(o =>
        {
            o.SenderEmail = SesAccount.Settings.SenderEmail!;
            o.SenderName = senderName;
            o.Region = SesAccount.Settings.Region;
        });
        return services.Build();
    }
}
