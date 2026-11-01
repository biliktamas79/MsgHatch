using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task ClaimDueAsync_MaxCountZero_ReturnsValidationFailed()
    {
        // Arrange
        await AddAsync(AMessage(T1));

        // Act
        var result = await ClaimAsync(maxCount: 0);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    public async Task ClaimDueAsync_ThreeDueMessages_ReturnsAtMostMaxCount(int maxCount, int expected)
    {
        // Arrange
        await TenantStore(T1).AddAsync([AMessage(T1), AMessage(T1), AMessage(T1)], Ct);

        // Act
        var result = await ClaimAsync(maxCount);

        // Assert
        Assert.Equal(expected, result.Value.Messages.Count);
    }

    [Fact]
    public async Task ClaimDueAsync_MessageThatIsNotDueYet_ClaimsNothingAndGivesItsTimeAsNextDue()
    {
        // Arrange
        var later = Now.AddSeconds(1);
        await AddAsync(AMessage(T1, nextTryAtUtc: later));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)later), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Theory]
    [InlineData(29, 0)]
    [InlineData(30, 1)]
    [InlineData(31, 1)]
    public async Task ClaimDueAsync_AfterAClaimWithALeaseOf30Seconds_ClaimsTheMessageAgainOnlyWhenTheLeaseEnds(
        int secondsLater, int expected)
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await ClaimAsync();
        Time.Advance(TimeSpan.FromSeconds(secondsLater));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(expected, result.Value.Messages.Count);
    }

    [Fact]
    public async Task ClaimDueAsync_DueMessage_SetsItsNextTryTimeAndLeaseToTheEndOfTheLease()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var leaseEnd = Now.AddSeconds(30);

        // Act
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);

        // Assert
        var stored = (await StoredAsync(message.Delivery.Id)).Delivery;
        Assert.Equal(
            [(leaseEnd, (DateTime?)leaseEnd), (leaseEnd, leaseEnd)],
            [(claimed.Delivery.NextTryAtUtc, claimed.Delivery.LeaseUntilUtc), (stored.NextTryAtUtc, stored.LeaseUntilUtc)]);
    }

    [Fact]
    public async Task ClaimDueAsync_DueMessagesOfTwoTenants_ReturnsEachAsItsClosedTypeWithItsTenantAndEnvelope()
    {
        // Arrange
        var ofT1 = await AddAsync(AMessage(T1, text: "Ősz-űr \"quoted\" <tag> & more"));
        var ofNoTenant = await AddAsync(AMessage(TenantKey.None, text: "second"));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(
            [(ofT1.Delivery.Id, ofT1.Tenant, typeof(OutgoingMessage<TestEnvelope>), (object)ofT1.Envelope),
             (ofNoTenant.Delivery.Id, null, typeof(OutgoingMessage<TestEnvelope>), ofNoTenant.Envelope)],
            result.Value.Messages.Select(static m => (m.Delivery.Id, m.Tenant, m.GetType(), m.Content)));
    }

    [Fact]
    public async Task ClaimDueAsync_TwoParallelClaims_GetDifferentMessages()
    {
        // Arrange
        await TenantStore(T1).AddAsync([.. Enumerable.Range(0, 10).Select(_ => AMessage(T1))], Ct);

        // Act
        var claims = await Task.WhenAll(Task.Run(() => ClaimAsync(5), Ct), Task.Run(() => ClaimAsync(5), Ct));

        // Assert
        Assert.Equal(10, claims.SelectMany(static claim => claim.Value.Messages).Select(static m => m.Delivery.Id).Distinct().Count());
    }

    [Fact]
    public async Task ClaimDueAsync_NoPendingMessage_GivesNoNextDueTime()
    {
        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)null), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_PendingMessageBeforeTheLeaseEnd_GivesItsTimeAsNextDue()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        var later = Now.AddSeconds(5);
        await AddAsync(AMessage(T1, nextTryAtUtc: later));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((1, (DateTime?)later), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_OnlyTheClaimedMessageIsPending_GivesItsLeaseEndAsNextDue()
    {
        // Arrange
        await AddAsync(AMessage(T1));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Now.AddSeconds(30), result.Value.NextDueAtUtc);
    }

    [Fact]
    public async Task ClaimDueAsync_MessageOfAnotherChannel_IsNotClaimedAndDoesNotCountForTheNextDueTime()
    {
        // Arrange
        await AddAsync(AMessage(T1, channel: TestChannel.SecondId, nextTryAtUtc: Now.AddSeconds(-1)));
        var ofTheChannel = await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddSeconds(5)));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)ofTheChannel.Delivery.NextTryAtUtc), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_MessagesOfTwoChannels_ClaimsOnlyTheMessagesOfItsChannel()
    {
        // Arrange
        var ofTheChannel = await AddAsync(AMessage(T1));
        var ofTheSecondChannel = await AddAsync(AMessage(T1, channel: TestChannel.SecondId));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(ofTheChannel.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(Now, (await StoredAsync(ofTheSecondChannel.Delivery.Id)).Delivery.NextTryAtUtc);
    }
}
