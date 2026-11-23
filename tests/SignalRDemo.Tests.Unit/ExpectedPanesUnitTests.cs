using MsgHatch.Domain.Routing;
using SignalRDemo.Notices;

namespace SignalRDemo.Tests.Unit;

/// <summary>
/// The panes that must get a notification (D316). The user <c>anna</c> is in <c>eu</c> and in <c>us</c>, the pane
/// <c>P5</c> is not connected, and the anonymous pane <c>P6</c> of <c>us</c> has the one company of its tenant.
/// </summary>
public sealed class ExpectedPanesUnitTests
{
    private static readonly PaneState[] _panes =
    [
        new("P1", Connected: true, "eu", "acme", "anna"),
        new("P2", Connected: true, "eu", "acme", "bela"),
        new("P3", Connected: true, "eu", Company: null, User: null),
        new("P4", Connected: true, "us", "globex", "anna"),
        new("P5", Connected: false, "eu", "acme", "bela"),
        new("P6", Connected: true, "us", "globex", User: null),
    ];

    [Theory]
    [InlineData(DeliveryTargetKind.Tenant, "eu", "", "P1", "P2", "P3")]
    [InlineData(DeliveryTargetKind.Company, "eu", "acme", "P1", "P2")]
    [InlineData(DeliveryTargetKind.Company, "us", "globex", "P4", "P6")]
    [InlineData(DeliveryTargetKind.User, "eu", "anna", "P1")]
    [InlineData(DeliveryTargetKind.User, "us", "anna", "P4")]
    public void Of_TargetAndPanesOfTwoTenants_GivesTheConnectedPanesOfTheTarget(
        DeliveryTargetKind kind, string tenant, string id, params string[] expected)
    {
        // Act
        var panes = ExpectedPanes.Of(tenant, new DeliveryTarget(kind, id), _panes);

        // Assert
        Assert.Equal(expected, panes);
    }
}
