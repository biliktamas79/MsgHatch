using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.SignalR.Tests.Unit;

/// <summary>The SignalR composer (section 4.8, step 4): one loader call, and one envelope with the payload for each route (D17, D135).</summary>
public sealed class SignalRComposerUnitTests
{
    private readonly IPayloadLoader<TestEvent, TestPayload> _loader = Substitute.For<IPayloadLoader<TestEvent, TestPayload>>();

    private readonly List<MessageRoute[]> _loadCalls = [];

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
        Assert.Equal(routes, Assert.Single(_loadCalls));
    }

    [Fact]
    public async Task ComposeAsync_PayloadsOfTheLoader_GivesOneEnvelopeWithThePayloadForEachRouteInTheOrderOfTheRoutes()
    {
        // Arrange
        TestPayload[] payloads = [new("first"), new("second")];
        LoaderGives(_ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue(payloads));

        // Act
        var result = await ComposeAsync(SignalRRoutes.AUserRoute(), SignalRRoutes.ACompanyRoute());

        // Assert
        Assert.Equal(payloads, result.Value.Select(static envelope => envelope.Value.Payload));
    }

    [Fact]
    public async Task ComposeAsync_LoaderError_ReturnsTheErrorOfTheLoaderAsTheOuterError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        LoaderGives(_ => error);

        // Act
        var result = await ComposeAsync(SignalRRoutes.AUserRoute());

        // Assert
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task ComposeAsync_LoaderGivesFewerPayloadsThanRoutes_ReturnsValidationFailedAsTheOuterError()
    {
        // Arrange
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("only one")]));

        // Act
        var result = await ComposeAsync(SignalRRoutes.AUserRoute("u1"), SignalRRoutes.AUserRoute("u2"));

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
    }

    [Fact]
    public async Task ComposeAsync_NullPayloadForOneRoute_FailsOnlyThatRouteWithValidationFailed()
    {
        // Arrange
        LoaderGives(static _ => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([new TestPayload("u1"), null!]));

        // Act
        var result = await ComposeAsync(SignalRRoutes.AUserRoute("u1"), SignalRRoutes.AUserRoute("u2"));

        // Assert
        Assert.Equal(
            [((ErrorType?)null, "u1"), (Errors.ValidationFailed, null)],
            result.Value.Select(static envelope => (envelope.Error?.Type, envelope.IsSuccess ? envelope.Value.Payload.Text : null)));
    }

    private void LoaderGives(Func<MessageRoute[], ErrorOr<IReadOnlyList<TestPayload>>> result) =>
        _loader.LoadAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            var routes = call.ArgAt<ReadOnlyMemory<MessageRoute>>(1).ToArray();
            _loadCalls.Add(routes);
            return ValueTask.FromResult(result(routes));
        });

    private ValueTask<ErrorOr<IReadOnlyList<ErrorOr<SignalREnvelope<TestPayload>>>>> ComposeAsync(params MessageRoute[] routes) =>
        new SignalRComposer<TestEvent, TestPayload>(_loader).ComposeAsync(TestMessages.AReceivedMessage(new TestEvent("1001")), routes, Ct);
}
