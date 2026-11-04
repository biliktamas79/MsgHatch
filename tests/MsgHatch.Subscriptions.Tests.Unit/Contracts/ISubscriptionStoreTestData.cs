namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>What the subscription store interface cannot do: reset the storage of all tenant keys.</summary>
public interface ISubscriptionStoreTestData
{
    ValueTask ResetAsync(CancellationToken ct);
}
