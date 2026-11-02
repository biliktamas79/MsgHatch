using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit;

/// <summary>The golden file of the email envelope (section 4.13), read with the content type of an email registration.</summary>
public sealed class MsgHatchJsonUnitTests
{
    [Fact]
    public async Task DeserializeOutgoing_EmailSampleOfVersion1_ReadsTheEmailEnvelope()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestEvent(static m => m.AsEmail<TestEvent, OrderEmail, TestOrderLoader>("OrderEmail"));
        await using var provider = services.Build();

        // Act
        var message = provider.GetRequiredService<MsgHatchJson>().DeserializeOutgoing(Sample("OutgoingEmailMessage.v1.json"));

        // Assert
        var email = Assert.IsType<OutgoingMessage<EmailMessage>>(message.Value).Envelope;
        Assert.Equal(
            ("client1@example.com", "Ödön Űr rendelése: 1001", "<p>Kedves Ödön &amp; \"Űr\"</p>", "Kedves Ödön", (string?)null, (IReadOnlyList<string>?)null),
            (email.To, email.Subject, email.HtmlBody, email.TextBody, email.From, email.Bcc));
        Assert.Equal(["copy@example.com", "second@example.com"], email.Cc!);
    }

    private static byte[] Sample(string fileName)
    {
        using var stream = typeof(MsgHatchJsonUnitTests).Assembly.GetManifestResourceStream($"MsgHatch.Email.Tests.Unit.Samples.{fileName}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
