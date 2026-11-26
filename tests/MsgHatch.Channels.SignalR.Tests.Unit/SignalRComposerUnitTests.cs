using MsgHatch.Channels.SignalR.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Channels.SignalR.Tests.Unit;

/// <summary>The SignalR composer (section 4.8, step 4): one loader call, and one envelope with the payload for each route (D17, D135).</summary>
public sealed class SignalRComposerUnitTests
{
    private readonly IPayloadLoader<TestEvent, TestPayload> _loader = Substitute.For<IPayloadLoader<TestEvent, TestPayload>>();

    private readonly ReceivedMessage<TestEvent> _message = TestMessages.AReceivedMessage(new TestEvent("1001"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ComposeAsync_Routes_CallsTheLoaderOneTimeWithAllRoutesInTheirOrder()
    {
        // Arrange
        LoaderGives(static routes => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([.. routes.Select(static route => new TestPayload(route.Target.Id))]));
        MessageRoute[] routes = [SignalRRoutes.AUserRoute("u1"), SignalRRoutes.ACompanyRoute("c1"), SignalRRoutes.AUserRoute("u2")];

        // Act
        await ComposeAsync(routes);

        // Assert
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    [Fact]
    public async Task ComposeAsync_PayloadsOfTheLoader_GivesOneEnvelopeWithThePayloadForEachRouteInTheOrderOfTheRoutes()
    {
        // Arrange
        TestPayload[] payloads = [new("first"), new("second")];
        LoaderGives(_ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue(payloads));
        MessageRoute[] routes = [SignalRRoutes.AUserRoute(), SignalRRoutes.ACompanyRoute()];

        // Act
        var result = await ComposeAsync(routes);

        // Assert
        Assert.Equal(payloads, result.Value.Select(static envelope => envelope.Value.Payload));
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    [Fact]
    public async Task ComposeAsync_LoaderError_ReturnsTheErrorOfTheLoaderAsTheOuterError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        LoaderGives(_ => error);
        var route = SignalRRoutes.AUserRoute();

        // Act
        var result = await ComposeAsync(route);

        // Assert
        Assert.Same(error, result.Error);
        await _loader.Received(1).LoadAsync(_message, RoutesAre([route]), Ct);
    }

    [Fact]
    public async Task ComposeAsync_LoaderGivesFewerPayloadsThanRoutes_ReturnsValidationFailedAsTheOuterError()
    {
        // Arrange
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("only one")]));
        MessageRoute[] routes = [SignalRRoutes.AUserRoute("u1"), SignalRRoutes.AUserRoute("u2")];

        // Act
        var result = await ComposeAsync(routes);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    [Fact]
    public async Task ComposeAsync_NullPayloadForOneRoute_FailsOnlyThatRouteWithValidationFailed()
    {
        // Arrange
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("u1"), null!]));
        MessageRoute[] routes = [SignalRRoutes.AUserRoute("u1"), SignalRRoutes.AUserRoute("u2")];

        // Act
        var result = await ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [((ErrorType?)null, "u1"), (Errors.ValidationFailed, null)],
            result.Value.Select(static envelope => (envelope.Error?.Type, envelope.IsSuccess ? envelope.Value.Payload.Text : null)));
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    private static ReadOnlyMemory<MessageRoute> RoutesAre(MessageRoute[] routes) =>
        Arg.Is<ReadOnlyMemory<MessageRoute>>(given => given.ToArray().SequenceEqual(routes));

    private void LoaderGives(Func<MessageRoute[], ErrorOr<IReadOnlyList<TestPayload>>> result) =>
        _loader.LoadAsync(default!, default, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(result(call.ArgAt<ReadOnlyMemory<MessageRoute>>(1).ToArray())));

    private ValueTask<ErrorOr<IReadOnlyList<ErrorOr<SignalREnvelope<TestPayload>>>>> ComposeAsync(params MessageRoute[] routes) =>
        new SignalRComposer<TestEvent, TestPayload>(_loader).ComposeAsync(_message, routes, Ct);
}
