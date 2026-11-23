using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using SignalRDemo.Notices;
using SignalRDemo.Sessions;
using SignalRDemo.Tests.Unit.TestTypes;

namespace SignalRDemo.Tests.Unit;

/// <summary>
/// The hub on a <c>TestServer</c> with SignalR sessions of the tenants <c>eu</c> and <c>us</c> (D315, D316). Each
/// notification goes through the router of the host, the outbox, and the SignalR transport. The user <c>anna</c> is in
/// both tenants, and <c>us</c> has one company, so its anonymous session also joins the group of that company.
/// </summary>
public sealed class DemoHubUnitTests
{
    [Theory]
    [InlineData(DeliveryTargetKind.Tenant, "eu", "", "eu anna", "eu anonymous", "eu bela", "eu cecil")]
    [InlineData(DeliveryTargetKind.Company, "eu", "acme", "eu anna", "eu bela")]
    [InlineData(DeliveryTargetKind.Company, "us", "globex", "us anna", "us anonymous")]
    [InlineData(DeliveryTargetKind.User, "eu", "anna", "eu anna")]
    [InlineData(DeliveryTargetKind.User, "us", "anna", "us anna")]
    public async Task Notification_SessionsOfTwoTenants_ReachesOnlyTheSessionsOfItsTarget(
        DeliveryTargetKind kind, string tenant, string id, params string[] expected)
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var demo = DemoWebApp.Build();
        await demo.App.StartAsync(ct);
        var sessions = await StartSessionsAsync(demo.App.GetTestServer(), ct);
        try
        {
            // Act
            var notification = await PublishAsync(demo.App, tenant, new DeliveryTarget(kind, id), ct);

            // Assert
            foreach (var session in sessions.Where(session => expected.Contains(session.Name)))
            {
                await session.WaitForAsync(notification, ct);
            }

            await WaitForAMarkerOfEachTenantAsync(demo.App, sessions, ct);
            Assert.Equal(expected, sessions.Where(session => session.HasReceived(notification)).Select(static session => session.Name).Order(StringComparer.Ordinal));
        }
        finally
        {
            await DisposeAsync(sessions);
        }
    }

    [Fact]
    public async Task Joined_SessionsWithATokenAndAnonymous_GivesTheirTenantCompanyUserAndGroups()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await using var demo = DemoWebApp.Build();
        await demo.App.StartAsync(ct);

        // Act
        var sessions = await StartSessionsAsync(demo.App.GetTestServer(), ct);

        // Assert
        try
        {
            string[] joined = [.. await Task.WhenAll(sessions.Select(static async session => Describe(await session.Joined)))];
            Assert.Equal(
                [
                    "eu acme anna eu:anna | msghatch:tenant:eu msghatch:eu:company:acme",
                    "eu acme bela eu:bela | msghatch:tenant:eu msghatch:eu:company:acme",
                    "eu initech cecil eu:cecil | msghatch:tenant:eu msghatch:eu:company:initech",
                    "eu - - - | msghatch:tenant:eu",
                    "us globex anna us:anna | msghatch:tenant:us msghatch:us:company:globex",
                    "us globex - - | msghatch:tenant:us msghatch:us:company:globex",
                ],
                joined);
        }
        finally
        {
            await DisposeAsync(sessions);
        }
    }

    private static async Task<DemoSession[]> StartSessionsAsync(TestServer server, CancellationToken ct) =>
    [
        await DemoSession.SignInAsync(server, "eu", "anna", "acme", ct),
        await DemoSession.SignInAsync(server, "eu", "bela", account: null, ct),
        await DemoSession.SignInAsync(server, "eu", "cecil", account: null, ct),
        await DemoSession.JoinTenantAsync(server, "eu", ct),
        await DemoSession.SignInAsync(server, "us", "anna", account: null, ct),
        await DemoSession.JoinTenantAsync(server, "us", ct),
    ];

    private static async Task<Guid> PublishAsync(WebApplication app, string tenant, DeliveryTarget target, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(new DemoNotice(target, "Test"), new MessageContext(id, new TenantId(tenant)), ct);
        Assert.Equal(1, result.Value.Stored);
        return id;
    }

    /// <summary>
    /// Sends one notification to each tenant after the notification of the test, and waits for it in each session. A
    /// session gets the notifications of one connection in their order, so a session that has its marker and not the
    /// notification of the test never gets that notification.
    /// </summary>
    private static async Task WaitForAMarkerOfEachTenantAsync(WebApplication app, DemoSession[] sessions, CancellationToken ct)
    {
        var markers = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["eu"] = await PublishAsync(app, "eu", new DeliveryTarget(DeliveryTargetKind.Tenant, string.Empty), ct),
            ["us"] = await PublishAsync(app, "us", new DeliveryTarget(DeliveryTargetKind.Tenant, string.Empty), ct),
        };
        foreach (var session in sessions)
        {
            await session.WaitForAsync(markers[session.Tenant], ct);
        }
    }

    private static string Describe(JoinedSession session) =>
        $"{session.Tenant} {session.Company ?? "-"} {session.User ?? "-"} {session.UserName ?? "-"} | {string.Join(' ', session.Groups)}";

    private static async Task DisposeAsync(DemoSession[] sessions)
    {
        foreach (var session in sessions)
        {
            await session.DisposeAsync();
        }
    }
}
