using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Performance;

/// <summary>
/// The subscription store of the benchmarks. A delivery read gives the subscriptions that it holds, from a result that
/// it made one time, so the store allocates nothing. The other members are not used.
/// </summary>
public sealed class PerfSubscriptionStore : ISubscriptionStore
{
    private static readonly ValueTask<ErrorOr<IReadOnlyList<Subscription>>> _subscriptions =
        ValueTask.FromResult(ErrorOr<IReadOnlyList<Subscription>>.ForValue(PerfSubscriptions.Subscriptions));

    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct) => _subscriptions;

    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct) => _subscriptions;

    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct) => ValueTask.FromResult(ErrorOr<Subscription?>.ForValue(null));

    public ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct) => ValueTask.FromResult<Error?>(null);

    public ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct) => ValueTask.FromResult<Error?>(null);

    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct) => ValueTask.FromResult<Error?>(null);
}
