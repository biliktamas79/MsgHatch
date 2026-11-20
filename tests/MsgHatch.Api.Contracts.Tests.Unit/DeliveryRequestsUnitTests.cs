using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The bodies of the bulk actions of the delivery API from the queries of the commands (D228, D291).</summary>
public sealed class DeliveryRequestsUnitTests
{
    private static readonly DateTime _time = new(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void From_OutboxQueryWithEachFilter_GivesTheBodyWithEachFilterAndNoLimitOrCursor()
    {
        // Arrange
        Guid[] ids = [Guid.NewGuid()];
        var source = Guid.NewGuid();
        var query = new OutboxQuery(
            ids, DeliveryStatus.Failed, new MessageType("OrderShipped"), new ChannelId("Email"), new DeliveryTarget(DeliveryTargetKind.Company, "acme"),
            source, _time.AddHours(-1), _time, _time.AddMinutes(-5), IncludeTest: true, Limit: 7, new MessageCursor(_time, Guid.NewGuid()));

        // Act
        var body = OutboxFilterRequest.From(query);

        // Assert
        Assert.Equal(
            new OutboxFilterRequest(
                ids, DeliveryStatus.Failed, "OrderShipped", "Email", DeliveryTargetKind.Company, "acme", source, _time.AddHours(-1), _time,
                _time.AddMinutes(-5), IncludeTest: true),
            body);
    }

    [Fact]
    public void From_OutboxQueryWithNoFilter_GivesTheBodyWithNoValue()
    {
        // Act
        var body = OutboxFilterRequest.From(new OutboxQuery());

        // Assert
        Assert.Equal(new OutboxFilterRequest(), body);
    }

    [Fact]
    public void From_InboxQueryWithEachFilter_GivesTheBodyWithEachFilterAndNoLimitOrCursor()
    {
        // Arrange
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var query = new InboxQuery(
            ids, DeliveryStatus.Pending, new MessageType("OrderShipped"), _time.AddHours(-1), _time, _time.AddMinutes(-5), IncludeTest: true,
            Limit: 3, new MessageCursor(_time, Guid.NewGuid()));

        // Act
        var body = InboxFilterRequest.From(query);

        // Assert
        Assert.Equal(
            new InboxFilterRequest(ids, DeliveryStatus.Pending, "OrderShipped", _time.AddHours(-1), _time, _time.AddMinutes(-5), IncludeTest: true),
            body);
    }
}
