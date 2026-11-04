using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class SubscriptionStoreTestsBase<TStore>
{
    [Theory]
    [MemberData(nameof(SubscriptionStoreTestRows.QueryRows), MemberType = typeof(SubscriptionStoreTestRows))]
    public async Task QueryAsync_ScenarioAndFilters_GivesTheSubscriptionsThatMatchAllFilters(
        string row, string? type, string? company, string? user, SubscriptionLevel? level, string expected)
    {
        // Arrange
        var query = new SubscriptionQuery(
            type is null ? null : new MessageType(type), company is null ? null : new CompanyId(company), user is null ? null : new UserId(user), level);

        // Act
        var subscriptions = await SeededStore.QueryAsync(query, Ct);

        // Assert
        Assert.Equal((row, expected), (row, Labels(subscriptions)));
    }
}
