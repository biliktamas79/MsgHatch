using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.Tests.Unit;

/// <summary>
/// The broker composer (section 4.8, step 4): one loader call, and one envelope for each route with the payload and the
/// headers of the channel settings of the route (D17, D135).
/// </summary>
public sealed class BrokerComposerUnitTests
{
    private readonly IPayloadLoader<TestEvent, TestPayload> _loader = Substitute.For<IPayloadLoader<TestEvent, TestPayload>>();

    private readonly List<MessageRoute[]> _loadCalls = [];

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
        Assert.Equal(routes, Assert.Single(_loadCalls));
    }

    [Fact]
    public async Task ComposeAsync_PayloadsOfTheLoader_GivesOneEnvelopeWithThePayloadAndTheSettingsHeadersOfEachRoute()
    {
        // Arrange
        TestPayload[] payloads = [new("first"), new("second")];
        var headers = new Dictionary<string, string> { ["x-source"] = "orders" };
        LoaderGives(_ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue(payloads));

        // Act
        var result = await ComposeAsync(BrokerRoutes.ACompanyRoute(headers: headers), BrokerRoutes.AUserRoute());

        // Assert
        Assert.Equal(
            [new BrokerEnvelope<TestPayload>(payloads[0], headers), new BrokerEnvelope<TestPayload>(payloads[1])],
            result.Value.Select(static envelope => envelope.Value));
    }

    [Fact]
    public async Task ComposeAsync_LoaderError_ReturnsTheErrorOfTheLoaderAsTheOuterError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        LoaderGives(_ => error);

        // Act
        var result = await ComposeAsync(BrokerRoutes.ACompanyRoute());

        // Assert
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task ComposeAsync_LoaderGivesFewerPayloadsThanRoutes_ReturnsValidationFailedAsTheOuterError()
    {
        // Arrange
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("only one")]));

        // Act
        var result = await ComposeAsync(BrokerRoutes.ACompanyRoute("c1"), BrokerRoutes.ACompanyRoute("c2"));

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task ComposeAsync_NullPayloadForOneRoute_FailsOnlyThatRouteWithValidationFailed()
    {
        // Arrange
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("c1"), null!]));

        // Act
        var result = await ComposeAsync(BrokerRoutes.ACompanyRoute("c1"), BrokerRoutes.ACompanyRoute("c2"));

        // Assert
        Assert.Equal(
            [((ErrorType?)null, "c1"), (Errors.ValidationFailed, null)],
            result.Value.Select(static envelope => (envelope.Error?.Type, envelope.IsSuccess ? envelope.Value.Payload.Text : null)));
    }

    private void LoaderGives(Func<MessageRoute[], ErrorOr<IReadOnlyList<TestPayload>>> result) =>
        _loader.LoadAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            var routes = call.ArgAt<ReadOnlyMemory<MessageRoute>>(1).ToArray();
            _loadCalls.Add(routes);
            return ValueTask.FromResult(result(routes));
        });

    private ValueTask<ErrorOr<IReadOnlyList<ErrorOr<BrokerEnvelope<TestPayload>>>>> ComposeAsync(params MessageRoute[] routes) =>
        new BrokerComposer<TestEvent, TestPayload>(_loader).ComposeAsync(TestMessages.AReceivedMessage(new TestEvent("1001")), routes, Ct);
}
