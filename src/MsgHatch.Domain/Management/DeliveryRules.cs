using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The stateless rules of a delivery request (section 4.19, D292). A rule that fails gives
/// <see cref="Errors.ValidationFailed"/>. The rules need no store and no clock, so a client runs them before a call (D234).
/// The check that the finish time of a purge is not after the current time needs the clock of the server, so the delivery
/// manager runs it after the rules.
/// </summary>
public static class DeliveryRules
{
    private const int _maxLimit = 500;
    private const int _maxIds = 1000;
    private const int _maxNoteLength = 500;

    /// <summary>Checks that the created times of a traffic query are in order.</summary>
    /// <param name="query">The query.</param>
    /// <returns>Null for a valid query, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static Error? Check(TrafficQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return CheckRange(query.CreatedFromUtc, query.CreatedToUtc);
    }

    /// <summary>Checks the limit of a page of the inbox, the ids, and the order of the created times.</summary>
    /// <param name="query">The query.</param>
    /// <returns>Null for a valid query, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static Error? Check(InboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return CheckPage(query.Ids, query.CreatedFromUtc, query.CreatedToUtc, query.Limit);
    }

    /// <summary>Checks the limit of a page of the outbox, the ids, and the order of the created times.</summary>
    /// <param name="query">The query.</param>
    /// <returns>Null for a valid query, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static Error? Check(OutboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return CheckPage(query.Ids, query.CreatedFromUtc, query.CreatedToUtc, query.Limit);
    }

    /// <summary>
    /// Checks the query of a retry or a cancel of the inbox: the rules of <see cref="Check(InboxQuery)"/>, and at least one
    /// filter, so that one call cannot change the whole store. The limit, the cursor, and the test flag are no filters.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>Null for a valid query, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static Error? CheckBulkAction(InboxQuery query) =>
        Check(query)
        ?? (query is { Ids: null, Status: null, Type: null, CreatedFromUtc: null, CreatedToUtc: null, DueBeforeUtc: null } ? NoFilter() : null);

    /// <summary>
    /// Checks the query of a retry or a cancel of the outbox: the rules of <see cref="Check(OutboxQuery)"/>, and at least one
    /// filter, so that one call cannot change the whole store. The limit, the cursor, and the test flag are no filters.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>Null for a valid query, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static Error? CheckBulkAction(OutboxQuery query) =>
        Check(query)
        ?? (query is
        {
            Ids: null, Status: null, Type: null, Channel: null, Target: null, SourceMessageId: null, CreatedFromUtc: null,
            CreatedToUtc: null, DueBeforeUtc: null,
        } ? NoFilter() : null);

    /// <summary>
    /// Checks an action on all tenants: a resume and a delete of the test data. A channel and the test data are global, so only
    /// the services of <see cref="TenantKey.None"/> change them (D106).
    /// </summary>
    /// <param name="key">The tenant key of the service.</param>
    /// <returns>Null for <see cref="TenantKey.None"/>, or the error.</returns>
    public static Error? CheckGlobalAction(TenantKey key) =>
        key.Tenant is null ? null
        : Invalid("A channel, the test data, and the purge are global, so only the manager with no tenant changes them (D106).");

    /// <summary>Checks a pause: an action on all tenants, with a note of at most 500 characters.</summary>
    /// <param name="key">The tenant key of the service.</param>
    /// <param name="note">The note, or null.</param>
    /// <returns>Null for a valid pause, or the error of the first rule that fails.</returns>
    public static Error? CheckPause(TenantKey key, string? note) =>
        CheckGlobalAction(key) ?? (note?.Length > _maxNoteLength ? Invalid($"The note has more than {_maxNoteLength} characters.") : null);

    /// <summary>
    /// Checks a purge: an action on all tenants, with a maximum count of at least 1. The delivery manager also checks that the
    /// finish time is not after the current time.
    /// </summary>
    /// <param name="key">The tenant key of the service.</param>
    /// <param name="maxCount">The maximum number of messages of each store.</param>
    /// <returns>Null for a valid purge, or the error of the first rule that fails.</returns>
    public static Error? CheckPurge(TenantKey key, int maxCount) =>
        CheckGlobalAction(key) ?? (maxCount < 1 ? Invalid("maxCount is less than 1.") : null);

    private static Error? CheckPage(IReadOnlyList<Guid>? ids, DateTime? createdFromUtc, DateTime? createdToUtc, int limit) =>
        limit is < 1 or > _maxLimit ? Invalid($"The limit is not 1 to {_maxLimit}.")
        : ids is { Count: 0 or > _maxIds } ? Invalid($"The ids are empty or more than {_maxIds}.")
        : CheckRange(createdFromUtc, createdToUtc);

    private static Error? CheckRange(DateTime? createdFromUtc, DateTime? createdToUtc) =>
        createdFromUtc >= createdToUtc ? Invalid("createdFrom is not before createdTo.") : null;

    private static Error NoFilter() => Invalid("A retry or a cancel needs at least one filter, so one call cannot change the whole store.");

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
