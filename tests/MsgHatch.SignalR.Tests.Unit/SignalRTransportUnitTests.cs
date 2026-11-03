using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.SignalR.Tests.Unit;

/// <summary>
/// The SignalR transport: the use cases of the transport contract, and the rules that only this transport has: the
/// user and group names of the targets, the method name, the redirect of a test message, and the exceptions of SignalR.
/// </summary>
public sealed class SignalRTransportUnitTests : TransportTestsBase<SignalRTransport<TestHub>>
{
    private readonly HubDoubles _hub;

    public SignalRTransportUnitTests()
        : this(new SignalRTransportTestDriver())
    {
    }

    private SignalRTransportUnitTests(SignalRTransportTestDriver driver)
        : base(new SignalRTransport<TestHub>(driver.Hub.Hub), driver) => _hub = driver.Hub;

    public static TheoryData<string, string?, string> UserRows => new()
    {
        { "no tenant", null, "u1" },
        { "a tenant", "t1", "t1:u1" },
    };

    public static TheoryData<string, string?, string> CompanyRows => new()
    {
        { "no tenant", null, "msghatch:company:c1" },
        { "a tenant", "t1", "msghatch:t1:company:c1" },
    };

    public static TheoryData<string, string?, DeliveryTarget> RedirectRows => new()
    {
        { "a user with no tenant", null, new DeliveryTarget(DeliveryTargetKind.User, "u1") },
        { "a company of a tenant", "t1", new DeliveryTarget(DeliveryTargetKind.Company, "c1") },
    };

    public static TheoryData<string, DeliveryTarget> InvalidTargetRows => new()
    {
        { "a user id that is empty", new DeliveryTarget(DeliveryTargetKind.User, "") },
        { "a company id with a control character", new DeliveryTarget(DeliveryTargetKind.Company, "c\n1") },
        { "an unknown target kind", new DeliveryTarget((DeliveryTargetKind)7, "u1") },
    };

    public static TheoryData<string, Exception, int> ExceptionRows => new()
    {
        { "JsonException of the hub protocol", new JsonException(), 403 },
        { "NotSupportedException of the hub protocol", new NotSupportedException(), 403 },
        { "IOException of a backplane", new IOException(), 503 },
        { "InvalidOperationException of a backplane", new InvalidOperationException(), 503 },
        { "TaskCanceledException of a timeout, with no cancelled token", new TaskCanceledException(), 503 },
    };

    [Theory]
    [MemberData(nameof(UserRows))]
    public async Task SendAsync_UserTarget_SendsThePayloadWithTheMessageTypeNameToTheUserName(string row, string? tenant, string expectedUser)
    {
        // Arrange
        var message = SignalRMessages.AnOutgoingMessage(tenant: TenantOf(tenant), target: new DeliveryTarget(DeliveryTargetKind.User, "u1"));

        // Act
        await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal((row, "User", expectedUser), (row, _hub.SingleName().Method, _hub.SingleName().Name));
        await _hub.Proxy.Received(1).SendCoreAsync("TestEvent", Arg.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], message.Envelope.Payload)), Ct);
    }

    [Theory]
    [MemberData(nameof(CompanyRows))]
    public async Task SendAsync_CompanyTarget_SendsThePayloadWithTheMessageTypeNameToTheGroupOfTheCompany(string row, string? tenant, string expectedGroup)
    {
        // Arrange
        var message = SignalRMessages.AnOutgoingMessage(tenant: TenantOf(tenant), target: new DeliveryTarget(DeliveryTargetKind.Company, "c1"));

        // Act
        await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal((row, "Group", expectedGroup), (row, _hub.SingleName().Method, _hub.SingleName().Name));
        await _hub.Proxy.Received(1).SendCoreAsync("TestEvent", Arg.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], message.Envelope.Payload)), Ct);
    }

    [Theory]
    [MemberData(nameof(RedirectRows))]
    public async Task SendAsync_RedirectTo_SendsToThatGroupInPlaceOfTheTarget(string row, string? tenant, DeliveryTarget target)
    {
        // Arrange
        var message = SignalRMessages.AnOutgoingMessage(tenant: TenantOf(tenant), target: target, flags: MessageFlags.Test);

        // Act
        await Transport.SendAsync(message, "testers", Ct);

        // Assert
        Assert.Equal((row, "Group", "testers"), (row, _hub.SingleName().Method, _hub.SingleName().Name));
        Assert.Same(message.Envelope.Payload, _hub.SentPayload());
    }

    [Fact]
    public async Task SendAsync_PayloadOfAnotherType_SendsThatPayload()
    {
        // Arrange
        var message = SignalRMessages.AnOutgoingMessage(new OtherPayload(7));

        // Act
        await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Same(message.Envelope.Payload, _hub.SentPayload());
    }

    [Fact]
    public async Task SendAsync_ContentThatIsNotASignalREnvelope_ReturnsValidationFailedAndSendsNothing()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(channel: SignalRChannel.Id);

        // Act
        var result = await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        Assert.Empty(_hub.Clients.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(InvalidTargetRows))]
    public async Task SendAsync_InvalidTarget_ReturnsValidationFailedAndSendsNothing(string row, DeliveryTarget target)
    {
        // Arrange
        var message = SignalRMessages.AnOutgoingMessage(target: target);

        // Act
        var result = await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal((row, Errors.ValidationFailed), (row, result.Error?.Type));
        Assert.Empty(_hub.Proxy.ReceivedCalls());
    }

    [Theory]
    [MemberData(nameof(ExceptionRows))]
    public async Task SendAsync_ExceptionOfSignalR_ReturnsTheErrorCodeOfTheRowWithTheException(string row, Exception exception, int expectedCode)
    {
        // Arrange
        _hub.Proxy.SendCoreAsync(default!, default!, Ct).ReturnsForAnyArgs(Task.FromException(exception));

        // Act
        var result = await Transport.SendAsync(SignalRMessages.AnOutgoingMessage(), redirectTo: null, Ct);

        // Assert
        Assert.Equal((row, expectedCode, exception), (row, result.Error?.Type.Code, result.Error?.Exception));
    }

    [Fact]
    public async Task SendAsync_ClassifierKnowsTheException_ReturnsItsErrorTypeBeforeTheBuiltInMapping()
    {
        // Arrange
        var transport = new SignalRTransport<TestHub>(_hub.Hub, [new ConflictClassifier<SignalRTransport<TestHub>>()]);
        _hub.Proxy.SendCoreAsync(default!, default!, Ct).ReturnsForAnyArgs(Task.FromException(new IOException()));

        // Act
        var result = await transport.SendAsync(SignalRMessages.AnOutgoingMessage(), redirectTo: null, Ct);

        // Assert
        Assert.Equal(Errors.Conflict, result.Error?.Type);
    }

    [Fact]
    public async Task SendAsync_CancelledTokenAndAClassifier_ReturnsOperationCancelled()
    {
        // Arrange
        var transport = new SignalRTransport<TestHub>(_hub.Hub, [new ConflictClassifier<SignalRTransport<TestHub>>()]);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();
        _hub.Proxy.SendCoreAsync(default!, default!, Ct).ReturnsForAnyArgs(Task.FromException(new OperationCanceledException()));

        // Act
        var result = await transport.SendAsync(SignalRMessages.AnOutgoingMessage(), redirectTo: null, stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    [Fact]
    public async Task CheckHealthAsync_SignalR_ReturnsNull()
    {
        // Act
        var error = await Transport.CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public void Channel_SignalRTransport_IsTheSignalRChannel()
    {
        // Act
        var channel = Transport.Channel;

        // Assert
        Assert.Equal(SignalRChannel.Id, channel);
    }

    private static TenantId? TenantOf(string? tenant) => tenant is null ? null : new TenantId(tenant);
}
