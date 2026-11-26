using MsgHatch.Channels.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Channels.Broker.Tests.Unit;

/// <summary>
/// The broker composer (section 4.8, step 4): one loader call, and one envelope for each route with the payload and the
/// headers of the channel settings of the route (D17, D135).
/// </summary>
public sealed class BrokerComposerUnitTests
{
    private readonly IPayloadLoader<TestEvent, TestPayload> _loader = Substitute.For<IPayloadLoader<TestEvent, TestPayload>>();

    private readonly ReceivedMessage<TestEvent> _message = TestMessages.AReceivedMessage(new TestEvent("1001"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ComposeAsync_Routes_CallsTheLoaderOneTimeWithAllRoutesInTheirOrder()
    {
        // Arrange
        LoaderGives(static routes => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([.. routes.Select(static route => new TestPayload(route.Target.Id))]));
        MessageRoute[] routes = [BrokerRoutes.ACompanyRoute("c1"), BrokerRoutes.AUserRoute("u1"), BrokerRoutes.ACompanyRoute("c2")];

        // Act
        await ComposeAsync(routes);

        // Assert
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    [Fact]
    public async Task ComposeAsync_PayloadsOfTheLoader_GivesOneEnvelopeWithThePayloadAndTheSettingsHeadersOfEachRoute()
    {
        // Arrange
        TestPayload[] payloads = [new("first"), new("second")];
        var headers = new Dictionary<string, string> { ["x-source"] = "orders" };
        LoaderGives(_ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue(payloads));
        MessageRoute[] routes = [BrokerRoutes.ACompanyRoute(headers: headers), BrokerRoutes.AUserRoute()];

        // Act
        var result = await ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [new BrokerEnvelope<TestPayload>(payloads[0], headers), new BrokerEnvelope<TestPayload>(payloads[1])],
            result.Value.Select(static envelope => envelope.Value));
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    [Fact]
    public async Task ComposeAsync_LoaderError_ReturnsTheErrorOfTheLoaderAsTheOuterError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        LoaderGives(_ => error);
        var route = BrokerRoutes.ACompanyRoute();

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
        MessageRoute[] routes = [BrokerRoutes.ACompanyRoute("c1"), BrokerRoutes.ACompanyRoute("c2")];

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
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("c1"), null!]));
        MessageRoute[] routes = [BrokerRoutes.ACompanyRoute("c1"), BrokerRoutes.ACompanyRoute("c2")];

        // Act
        var result = await ComposeAsync(routes);

        // Assert
        Assert.Equal(
            [((ErrorType?)null, "c1"), (Errors.ValidationFailed, null)],
            result.Value.Select(static envelope => (envelope.Error?.Type, envelope.IsSuccess ? envelope.Value.Payload.Text : null)));
        await _loader.Received(1).LoadAsync(_message, RoutesAre(routes), Ct);
    }

    private static ReadOnlyMemory<MessageRoute> RoutesAre(MessageRoute[] routes) =>
        Arg.Is<ReadOnlyMemory<MessageRoute>>(given => given.ToArray().SequenceEqual(routes));

    private void LoaderGives(Func<MessageRoute[], ErrorOr<IReadOnlyList<TestPayload>>> result) =>
        _loader.LoadAsync(default!, default, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(result(call.ArgAt<ReadOnlyMemory<MessageRoute>>(1).ToArray())));

    private ValueTask<ErrorOr<IReadOnlyList<ErrorOr<BrokerEnvelope<TestPayload>>>>> ComposeAsync(params MessageRoute[] routes) =>
        new BrokerComposer<TestEvent, TestPayload>(_loader).ComposeAsync(_message, routes, Ct);
}
