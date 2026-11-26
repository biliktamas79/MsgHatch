using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The behavior of the inbox composite with a store for each tenant (D125, D173, section 4.5).</summary>
public sealed partial class TenantInboxDispatchStoreUnitTests
{
    [Fact]
    public async Task AddAsync_TenantStoresOfTwoKeys_WriteOnlyIntoTheStoreOfTheirKey()
    {
        // Arrange
        var ofT1 = AMessage(T1);
        var ofT2 = AMessage(T2);

        // Act
        await TenantStore(T1).AddAsync(ofT1, Ct);
        await TenantStore(T2).AddAsync(ofT2, Ct);

        // Assert
        Assert.Equal([[ofT1.Delivery.Id], [ofT2.Delivery.Id]], [IdsIn(Stores.InboxOf(T1)), IdsIn(Stores.InboxOf(T2))]);
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
        await Store.FailAsync(claimed, "failed", Now, nextTryAtUtc: null, Ct);
        await ClaimAsync();
        await TenantStore(T1).RetryAsync([message.Delivery.Id], Now, 1, Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
    }

    [Fact]
    public async Task CompleteAsync_MessageOfATenant_GoesOnlyToTheStoreOfThatTenant()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);

        // Act
        var error = await Store.CompleteAsync(claimed, Now, Ct);

        // Assert
        Assert.Equal(
            ((Error?)null, DeliveryStatus.Done, 0),
            (error, Assert.Single(Stores.InboxOf(T1).GetMessages()).Delivery.Status, Stores.InboxOf(TenantKey.None).GetMessages().Count));
        Assert.Equal(message.Delivery.Id, claimed.Delivery.Id);
    }

    [Fact]
    public async Task FailAsync_NextTryBeforeTheLeaseEnd_GoesToTheStoreOfTheTenantAndAsksItAtTheNextTryTime()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        var claimed = Assert.Single((await ClaimAsync()).Value.Messages);
        var error = await Store.FailAsync(claimed, "busy", Now, Now.AddSeconds(5), Ct);
        Time.Advance(TimeSpan.FromSeconds(5));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(((Error?)null, message.Delivery.Id), (error, Assert.Single(result.Value.Messages).Delivery.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_StoreOfATenantFails_SkipsItLogsItsTenantWithNoChannelAndClaimsTheOtherStores()
    {
        // Arrange
        var failing = Substitute.For<IInboxDispatchStore>();
        failing.ClaimDueAsync(default, default, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<ClaimResult<ReceivedMessage>>>(new Error(Errors.SerializationFailed)));
        Stores.Listed.Add(T1.Tenant!.Value);
        Stores.InboxDispatchStores[T1] = failing;
        var ofT2 = await AddAsync(AMessage(T2));

        // Act
        var result = await ClaimAsync();

        // Assert
        var record = Assert.Single(Stores.Provider.LogRecords());
        Assert.Equal(ofT2.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            (1502, LogLevel.Error, "t1", (string?)null),
            (record.Id.Id, record.Level, record.GetStructuredStateValue("TenantId"), record.GetStructuredStateValue("Channel")));
        _ = failing.Received(1).ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
    }

    private static Guid[] IdsIn(InMemoryInboxStore store) => [.. store.GetMessages().Select(static message => message.Delivery.Id)];
}
