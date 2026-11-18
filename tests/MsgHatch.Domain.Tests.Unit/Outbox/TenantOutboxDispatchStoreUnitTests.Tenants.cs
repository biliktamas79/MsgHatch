using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.Core;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>The behavior of the outbox composite with a store for each tenant (D125, D173, section 4.5).</summary>
public sealed partial class TenantOutboxDispatchStoreUnitTests
{
    public static TheoryData<int, int> MaxPollIntervalRows => new()
    {
        { 29, 0 },
        { 30, 1 },
        { 31, 1 },
    };

    [Fact]
    public async Task AddAsync_TenantStoresOfTwoKeys_WriteOnlyIntoTheStoreOfTheirKey()
    {
        // Arrange
        var ofT1 = AMessage(T1);
        var ofT2 = AMessage(T2);

        // Act
        await TenantStore(T1).AddAsync([ofT1], Ct);
        await TenantStore(T2).AddAsync([ofT2], Ct);

        // Assert
        Assert.Equal([[ofT1.Delivery.Id], [ofT2.Delivery.Id]], [IdsIn(Stores.OutboxOf(T1)), IdsIn(Stores.OutboxOf(T2))]);
    }

    [Fact]
    public async Task ClaimDueAsync_MessagesOfTwoTenants_TakesTheTenantsInTurnFromTheOneAfterTheLastGiver()
    {
        // Arrange
        var firstOfT1 = await AddAsync(AMessage(T1));
        var secondOfT1 = await AddAsync(AMessage(T1));
        var firstOfT2 = await AddAsync(AMessage(T2));

        // Act
        var first = await ClaimAsync(maxCount: 1);
        var second = await ClaimAsync(maxCount: 1);
        var third = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal(
            [firstOfT1.Delivery.Id, firstOfT2.Delivery.Id, secondOfT1.Delivery.Id],
            first.Value.Messages.Concat(second.Value.Messages).Concat(third.Value.Messages).Select(static message => message.Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_LastGiverThatTheSourceNoLongerGives_StartsTheTurnAtTheFirstTenant()
    {
        // Arrange
        var t3 = TenantKey.For(new TenantId("t3"));
        await AddAsync(AMessage(T1));
        var ofT2 = await AddAsync(AMessage(T2));
        await AddAsync(AMessage(t3));
        await ClaimAsync(maxCount: 1);
        Stores.Unlisted.Add(T1.Tenant!.Value);

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal(ofT2.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_TenantThatGaveNoMessage_DoesNotMoveTheTurn()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T2, nextTryAtUtc: Now.AddMinutes(1)));
        await ClaimAsync();
        await AddAsync(AMessage(T1));
        var ofT2 = await AddAsync(AMessage(T2));

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal(ofT2.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_MaxCountMessagesFromTheFirstTenant_AsksNoOtherTenantAndGivesNowAsTheNextDueTime()
    {
        // Arrange
        var ofT1 = await AddAsync(AMessage(T1));
        await AddAsync(AMessage(T2, nextTryAtUtc: Now.AddSeconds(10)));

        // Act
        var result = await ClaimAsync(maxCount: 1);

        // Assert
        Assert.Equal((ofT1.Delivery.Id, (DateTime?)Now), (Assert.Single(result.Value.Messages).Delivery.Id, result.Value.NextDueAtUtc));
        Assert.Empty(Stores.Provider.LogRecords());
    }

    [Fact]
    public async Task ClaimDueAsync_PendingMessagesInThreeStores_GivesTheEarliestNextDueTimeOfAllStores()
    {
        // Arrange
        var earliest = Now.AddSeconds(5);
        await AddAsync(AMessage(TenantKey.None, nextTryAtUtc: Now.AddSeconds(20)));
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddSeconds(10)));
        await AddAsync(AMessage(T2, nextTryAtUtc: earliest));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)earliest), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_TenantWithNoDueMessage_IsNotAskedBeforeItsNextDueTime()
    {
        // Arrange
        var nextDue = Now.AddSeconds(10);
        await AddAsync(AMessage(T1, nextTryAtUtc: nextDue));
        await ClaimAsync();
        await Stores.OutboxOf(T1).For(T1).AddAsync([AMessage(T1)], Ct);
        Time.Advance(TimeSpan.FromSeconds(5));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, (DateTime?)nextDue), (result.Value.Messages.Count, result.Value.NextDueAtUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_AfterAnAddThroughTheTenantStoreOfThatTenant_AsksTheTenantAgain()
    {
        // Arrange
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddMinutes(1)));
        await ClaimAsync();
        var added = await AddAsync(AMessage(T1));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(added.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_AfterARetryThroughTheTenantStoreOfThatTenant_AsksTheTenantAgain()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        await Store.RecordTryAsync(claimed, ATry(claimed, PublishOutcome.PermanentError), nextTryAtUtc: null, Ct);
        await ClaimAsync();
        await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, 1, Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Theory]
    [MemberData(nameof(MaxPollIntervalRows))]
    public async Task ClaimDueAsync_MessageThatAnotherStoreInstanceAdds_IsFoundAfterMaxPollInterval(int seconds, int expected)
    {
        // Arrange
        Stores.Listed.Add(T1.Tenant!.Value);
        await ClaimAsync();
        await Stores.OutboxOf(T1).For(T1).AddAsync([AMessage(T1)], Ct);
        Time.Advance(TimeSpan.FromSeconds(seconds));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(expected, result.Value.Messages.Count);
    }

    [Fact]
    public async Task ClaimDueAsync_AddThroughTheTenantStoreWhileTheClaimAsksTheTenant_AsksTheTenantAgainInTheNextClaim()
    {
        // Arrange
        var added = AMessage(T1);
        var inner = Stores.OutboxOf(T1);
        var store = Substitute.For<IOutboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, default, Ct).ReturnsForAnyArgs(ClaimThenAddAsync);
        Stores.OutboxDispatchStores[T1] = store;
        await ClaimAsync();

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(added.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);

        async ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimThenAddAsync(CallInfo call)
        {
            var claim = await inner.ClaimDueAsync(call.ArgAt<ChannelId>(0), call.ArgAt<int>(1), call.ArgAt<DateTime>(2), call.ArgAt<TimeSpan>(3), Ct);
            await TenantStore(T1).AddAsync([added], Ct);
            return claim;
        }
    }

    [Fact]
    public async Task RecordTryAsync_MessageOfATenant_GoesOnlyToTheStoreOfThatTenant()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);

        // Act
        var error = await Store.RecordTryAsync(claimed, ATry(claimed, PublishOutcome.Succeeded), nextTryAtUtc: null, Ct);

        // Assert
        Assert.Equal(((Error?)null, 1, 0), (error, Stores.OutboxOf(T1).GetTries(claimed.Delivery.Id).Count, Stores.OutboxOf(TenantKey.None).GetMessages().Count));
    }

    [Fact]
    public async Task RecordTryAsync_NextTryBeforeTheLeaseEnd_AsksTheTenantAtTheNextTryTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        await Store.RecordTryAsync(claimed, ATry(claimed, PublishOutcome.TransientError), Now.AddSeconds(5), Ct);
        Time.Advance(TimeSpan.FromSeconds(5));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task ClaimDueAsync_DownStateInTheStoreOfNoTenant_ReturnsTheStateAndClaimsNoMessageOfATenant()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var down = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");
        await Store.SetChannelDownAsync(down, Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((down, 0, (DateTime?)null), (result.Value.Down, result.Value.Messages.Count, (await StoredAsync(message.Delivery.Id)).Delivery.LeaseUntilUtc));
        Assert.Equal(
            [[down], []],
            [(await Stores.OutboxOf(TenantKey.None).GetDownChannelsAsync(Ct)).Value, (await Stores.OutboxOf(T1).GetDownChannelsAsync(Ct)).Value]);
    }

    [Fact]
    public async Task SetChannelDownAsync_Composite_AddsTheRowOnlyInTheStoreOfNoTenant()
    {
        // Arrange
        var down = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");

        // Act
        await Store.SetChannelDownAsync(down, Ct);

        // Assert
        Assert.Equal(
            [[down], [down], []],
            [
                (await Store.GetChannelStatesAsync(Now, Ct)).Value.Select(static row => row.ToDown()),
                (await Stores.OutboxOf(TenantKey.None).GetChannelStatesAsync(Now, Ct)).Value.Select(static row => row.ToDown()),
                (await Stores.OutboxOf(T1).GetChannelStatesAsync(Now, Ct)).Value.Select(static row => row.ToDown()),
            ]);
    }

    [Fact]
    public async Task ClaimDueAsync_StoreOfATenantFails_SkipsItLogsItsTenantAndClaimsTheOtherStores()
    {
        // Arrange
        Stores.Listed.Add(T1.Tenant!.Value);
        Stores.OutboxDispatchStores[T1] = AStoreThatFails(new Error(Errors.Unavailable));
        var ofT2 = await AddAsync(AMessage(T2));

        // Act
        var result = await ClaimAsync();

        // Assert
        var record = Assert.Single(Stores.Provider.LogRecords());
        Assert.Equal(ofT2.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            (1502, LogLevel.Warning, "t1", "Test"),
            (record.Id.Id, record.Level, record.GetStructuredStateValue("TenantId"), record.GetStructuredStateValue("Channel")));
    }

    [Fact]
    public async Task ClaimDueAsync_StoreOfNoTenantFails_ReturnsItsErrorAndAsksNoTenant()
    {
        // Arrange
        Stores.OutboxDispatchStores[TenantKey.None] = AStoreThatFails(new Error(Errors.Unavailable));
        var message = await AddAsync(AMessage(T2));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((Errors.Unavailable, (DateTime?)null), (result.Error?.Type, (await StoredAsync(message.Delivery.Id)).Delivery.LeaseUntilUtc));
    }

    [Fact]
    public async Task ClaimDueAsync_StoreOfATenantIsCancelled_ReturnsOperationCancelled()
    {
        // Arrange
        Stores.Listed.Add(T1.Tenant!.Value);
        Stores.OutboxDispatchStores[T1] = AStoreThatFails(new Error(Errors.OperationCancelled));
        await AddAsync(AMessage(T2));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_TenantSourceFails_ReturnsItsErrorAndClaimsNoMessage()
    {
        // Arrange
        var message = await AddAsync(AMessage(TenantKey.None));
        Stores.TenantsError = new Error(Errors.Unavailable);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((Errors.Unavailable, (DateTime?)null), (result.Error?.Type, (await StoredAsync(message.Delivery.Id)).Delivery.LeaseUntilUtc));
    }

    [Fact]
    public async Task CountAsync_TenantSourceFails_ReturnsItsError()
    {
        // Arrange
        Stores.TenantsError = new Error(Errors.Unavailable);

        // Act
        var result = await Store.CountAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task DeleteTestDataAsync_TenantSourceFails_ReturnsItsErrorAndDeletesNothing()
    {
        // Arrange
        var message = await AddAsync(AMessage(TenantKey.None, flags: MessageFlags.Test));
        Stores.TenantsError = new Error(Errors.Unavailable);

        // Act
        var result = await Store.DeleteTestDataAsync(Ct);

        // Assert
        Assert.Equal((Errors.Unavailable, message.Delivery.Id), (result.Error?.Type, (await StoredAsync(message.Delivery.Id)).Delivery.Id));
    }

    [Fact]
    public async Task CountAsync_StoreOfATenantFails_ReturnsItsError()
    {
        // Arrange
        Stores.Listed.Add(T1.Tenant!.Value);
        Stores.OutboxDispatchStores[T1] = AStoreThatFails(new Error(Errors.Unavailable));

        // Act
        var result = await Store.CountAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task PurgeAsync_StoreOfATenantFails_ReturnsItsError()
    {
        // Arrange
        Stores.Listed.Add(T1.Tenant!.Value);
        Stores.OutboxDispatchStores[T1] = AStoreThatFails(new Error(Errors.Unavailable));

        // Act
        var result = await Store.PurgeAsync(Now, 10, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    private static Guid[] IdsIn(InMemoryOutboxStore store) => [.. store.GetMessages().Select(static message => message.Delivery.Id)];

    private static IOutboxDispatchStore AStoreThatFails(Error error)
    {
        var store = Substitute.For<IOutboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, default, default)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<ClaimResult<OutgoingMessage>>>(error));
        store.CountAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageCount>>>(error));
        store.PurgeAsync(default, default, default).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<int>>(error));
        return store;
    }

    private PublishTry ATry(OutgoingMessage message, PublishOutcome outcome) =>
        new(message.Delivery.Id, message.Delivery.TryCount + 1, Now, Now, outcome, ProviderMessageId: null,
            ErrorCode: outcome == PublishOutcome.Succeeded ? null : 503, ErrorName: outcome == PublishOutcome.Succeeded ? null : "Unavailable", ErrorMessage: null);
}
