using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>The test messages and the previews (D82, D84).</summary>
public sealed partial class NotificationServiceUnitTests
{
    [Fact]
    public async Task ProcessAsync_TestFlagInTheContext_GoesToEachOutgoingMessage()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, AContext(flags: MessageFlags.Test));

        // Assert
        Assert.Equal([MessageFlags.Test, MessageFlags.Test], _doubles.Stored.Select(static m => m.Flags));
    }

    [Fact]
    public async Task PreviewAsync_Routes_StoresNothingCallsNoRouterAndReturnsOneResultForEachRouteInOrder()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1"), ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings()), ARoute("u3")];
        await using var provider = _doubles.Build(_time);

        // Act
        var results = await PreviewAsync(provider, AContext(), routes);

        // Assert
        Assert.Equal(routes, results.Value.Select(static r => r.Route));
        Assert.Empty(_doubles.Stored);
        _ = _doubles.Router(TenantKey.None).DidNotReceiveWithAnyArgs().RouteAsync(default!, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task PreviewAsync_Routes_EachResultHasTheOutgoingMessageThatProcessAsyncStoresWithTheTestFlag()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1", trace: new TestTrace("first")), ARoute("u2", address: "client2@example.com")];
        _doubles.Routes = routes;
        var context = AContext(_tenant);
        await using var provider = _doubles.Build(_time);
        await ProcessAsync(provider, context with { Flags = MessageFlags.Test });

        // Act
        var results = await PreviewAsync(provider, context, routes);

        // Assert
        Assert.Equal(_doubles.Stored, results.Value.Select(static r => r.Message));
        Assert.Equal([MessageFlags.Test, MessageFlags.Test], results.Value.Select(static r => r.Message!.Flags));
    }

    [Fact]
    public async Task PreviewAsync_RouteThatFailsACheck_GivesAResultWithItsError()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1"), ARoute("u1", address: "second@example.com")]; // the second route has the same target and channel
        await using var provider = _doubles.Build(_time);

        // Act
        var results = await PreviewAsync(provider, AContext(), routes);

        // Assert
        Assert.Equal(
            [(true, (int?)null), (false, 409)],
            results.Value.Select(static r => (r.Message is not null, r.Error?.Type.Code)));
    }

    [Fact]
    public async Task PreviewAsync_NullRoutes_CallsTheRouterOneTimeAndReturnsItsRoutes()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1"), ARoute("u2")];
        _doubles.Routes = routes;
        await using var provider = _doubles.Build(_time);

        // Act
        var results = await PreviewAsync(provider, AContext(), routes: null);

        // Assert
        Assert.Equal(routes, results.Value.Select(static r => r.Route));
        _ = _doubles.Router(TenantKey.None).ReceivedWithAnyArgs(1).RouteAsync(default!, Ct);
        Assert.Empty(_doubles.Stored);
    }

    [Fact]
    public async Task PreviewAsync_UnregisteredEventType_ReturnsNotFound()
    {
        // Arrange
        await using var provider = _doubles.Build(_time);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Act
        var results = await service.PreviewAsync(new OtherTestEvent("text"), AContext(), routes: null, Ct);

        // Assert
        Assert.Equal(Errors.NotFound, results.Error?.Type);
    }

    private static async Task<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync(
        IServiceProvider provider, MessageContext context, IReadOnlyList<MessageRoute>? routes)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .PreviewAsync(new TestEvent("text"), context, routes, Ct);
    }
}
