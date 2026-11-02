using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit;

/// <summary>
/// The email transport: the use cases of the transport contract, and the rules that only this transport has: the
/// envelope and the id that it gives to the email client, and the redirect of a test message (section 4.8).
/// </summary>
public sealed class EmailTransportUnitTests : TransportTestsBase<EmailTransport>
{
    private readonly IEmailClient _client;

    public EmailTransportUnitTests()
        : this(new EmailTransportTestDriver())
    {
    }

    private EmailTransportUnitTests(EmailTransportTestDriver driver)
        : base(new EmailTransport(driver.Client), driver) => _client = driver.Client;

    [Fact]
    public async Task SendAsync_OutgoingEmail_GivesTheEmailOfTheContentAndTheOutgoingMessageIdToTheClient()
    {
        // Arrange
        var (id, email) = (Guid.NewGuid(), TestMessages.AnEmail());

        // Act
        await Transport.SendAsync(TestMessages.AnOutgoingEmail(email, id), redirectTo: null, Ct);

        // Assert
        await _client.Received(1).SendAsync(id, Arg.Is<EmailMessage>(sent => ReferenceEquals(sent, email)), Ct);
    }

    [Fact]
    public async Task SendAsync_RedirectTo_ReplacesTheRecipientAndRemovesTheCopies()
    {
        // Arrange
        var email = TestMessages.AnEmail(to: "client1@example.com", cc: ["cc1@example.com", "cc2@example.com"], bcc: ["bcc1@example.com", "bcc2@example.com"]);

        // Act
        await Transport.SendAsync(TestMessages.AnOutgoingEmail(email), "tester@example.com", Ct);

        // Assert
        var sent = (EmailMessage)_client.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.Equal(email with { To = "tester@example.com", Cc = null, Bcc = null }, sent);
    }

    [Fact]
    public async Task SendAsync_ContentThatIsNotAnEmail_ReturnsValidationFailedAndCallsNoClient()
    {
        // Arrange
        var message = new OutgoingMessage<string>(
            1, TestMessages.AnOutgoingEmail().Delivery, Guid.NewGuid(), Tenant: null, new MessageType("TestEvent"), EmailChannel.Id,
            new DeliveryTarget(DeliveryTargetKind.User, "u1"), "Email:OrderEmail", 1, MessageFlags.None, TraceParent: null, Trace: null,
            "not an email");

        // Act
        var result = await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        Assert.Empty(_client.ReceivedCalls());
    }

    [Fact]
    public async Task CheckHealthAsync_ErrorOfTheClient_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        _client.CheckHealthAsync(Ct).Returns(ValueTask.FromResult<Error?>(expected));

        // Act
        var error = await Transport.CheckHealthAsync(Ct);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public void Channel_EmailTransport_IsTheEmailChannel()
    {
        // Act
        var channel = Transport.Channel;

        // Assert
        Assert.Equal(EmailChannel.Id, channel);
    }
}
