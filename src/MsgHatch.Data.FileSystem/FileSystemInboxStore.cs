using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The inbox in JSON files (section 4.5, D26): one file for each received message, written with
/// <see cref="MsgHatchJson"/>, under <c>{RootPath}/inbox</c>, and each test message under <c>{RootPath}/test/inbox</c>.
/// A claim moves a file from <c>pending</c> to <c>claimed</c> with an atomic rename. It is the dispatch store of all
/// tenants, and <see cref="For"/> gives the tenant store of one key.
/// </summary>
// ponytail: rename-based claims suit one machine or a shared folder with atomic rename, use a database store for more throughput
public sealed class FileSystemInboxStore : IInboxDispatchStore
{
    private readonly MsgHatchJson _json;
    private readonly MessageFiles _files = new();
    private readonly string _root;
    private readonly string _testRoot;
    private readonly string[] _folders;

    /// <summary>Makes the store on a folder.</summary>
    /// <param name="rootPath">The root folder of the file stores. The inbox is under <c>{rootPath}/inbox</c>.</param>
    /// <param name="json">The serializer of the messages.</param>
    public FileSystemInboxStore(string rootPath, MsgHatchJson json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(json);
        RootPath = rootPath;
        _json = json;
        _root = Path.Combine(rootPath, "inbox");
        _testRoot = Path.Combine(rootPath, "test", "inbox");
        _folders = [_root, _testRoot];
    }

    /// <summary>The root folder of the file stores.</summary>
    public string RootPath { get; }

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IInboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : FileFailures.Run(() => Claim(maxCount, nowUtc, nowUtc + lease)));

    /// <inheritdoc />
    public ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(Update(message, delivery => delivery with
        {
            Status = DeliveryStatus.Done,
            TryCount = delivery.TryCount + 1,
            LastTryAtUtc = triedAtUtc,
            LeaseUntilUtc = null,
        }));

    /// <inheritdoc />
    public ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(Update(message, delivery => delivery with
        {
            Status = nextTryAtUtc is null ? DeliveryStatus.Failed : DeliveryStatus.Pending,
            TryCount = delivery.TryCount + 1,
            LastTryAtUtc = triedAtUtc,
            NextTryAtUtc = nextTryAtUtc ?? delivery.NextTryAtUtc,
            LeaseUntilUtc = null,
            LastError = error,
        }));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => ErrorOr<int>.ForValue(MessageFiles.DeleteRoot(_testRoot))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : FileFailures.Run(() => ErrorOr<int>.ForValue(Purge(finishedBeforeUtc, maxCount))));

    private int Purge(DateTime finishedBeforeUtc, int maxCount)
    {
        (string Root, string Path, ReceivedMessage Message)[] purged =
        [
            .. _folders
                .SelectMany(root => ReadEach(root, MessageFiles.FinishedStatuses).Select(file => (root, file.Path, file.Message)))
                .Where(file => file.Message.Delivery.FinishedBefore(finishedBeforeUtc))
                .OrderBy(static file => file.Message.Delivery.FinishedAtUtc())
                .Take(maxCount),
        ];
        foreach (var (root, path, message) in purged)
        {
            MessageFiles.DeletePurged(path, MessageFiles.MarkerOf(root, message.Tenant, message.Delivery.Id));
        }

        return purged.Length;
    }

    private MessagePage<ReceivedMessageSummary> Query(TenantKey key, InboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return MessagePage.Of(
            ReadTenant(key, query.IncludeTest, query.Status).Where(query.Matches),
            static message => message.Delivery, ReceivedMessageSummary.Of, query.After, query.Limit);
    }

    private ErrorOr<ReceivedMessage?> Get(TenantKey key, Guid id) =>
        PathOf(key, id) is { } path
            ? _json.DeserializeReceived(File.ReadAllBytes(path)).ThenIfSuccess(static message => ErrorOr<ReceivedMessage?>.ForValue(message))
            : ErrorOr<ReceivedMessage?>.ForValue(null);

    private IReadOnlyList<MessageCount> Count(TenantKey key, TrafficQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return MessageCount.Of(ReadTenant(key, query.IncludeTest, query.Status)
            .Where(message => query.Matches(message.Delivery, message.Flags))
            .Select(static message => (message.Type, (ChannelId?)null, message.Delivery, message.Flags)));
    }

    /// <summary>
    /// Changes the delivery state of each readable message of the tenant with one of the ids, when the change gives a
    /// new state. It moves the file into the folder of its new status.
    /// </summary>
    private int Change(TenantKey key, IReadOnlyList<Guid> ids, Func<DeliveryState, DeliveryState?> change)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return _files.WhileNoClaimRuns(() =>
        {
            var changed = 0;
            foreach (var path in ids.Select(id => PathOf(key, id)).OfType<string>())
            {
                var stored = _json.DeserializeReceived(File.ReadAllBytes(path));
                if (stored.IsSuccess && change(stored.Value.Delivery) is { } delivery
                    && _json.Serialize(stored.Value with { Delivery = delivery }) is { IsSuccess: true } content)
                {
                    MessageFiles.Replace(path, delivery, content.Value);
                    changed++;
                }
            }

            return changed;
        });
    }

    private string? PathOf(TenantKey key, Guid id) =>
        _folders.Select(root => MessageFiles.Find(root, MessageFiles.KeyOf(id, key.Tenant))).FirstOrDefault(static path => path is not null);

    private IEnumerable<ReceivedMessage> ReadTenant(TenantKey key, bool includeTest, DeliveryStatus? status) =>
        (includeTest ? _folders : [_root])
            .SelectMany(root => ReadEach(root, status is { } value ? [value] : Enum.GetValues<DeliveryStatus>()))
            .Select(static file => file.Message)
            .Where(message => message.Tenant == key.Tenant);

    private IEnumerable<(string Path, ReceivedMessage Message)> ReadEach(string root, IEnumerable<DeliveryStatus> statuses) =>
        MessageFiles.ReadEach(MessageFiles.FilesOf(root, statuses), content => _json.DeserializeReceived(content));

    private ErrorOr<ClaimResult<ReceivedMessage>> Claim(int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        var claimed = new List<ReceivedMessage>();
        foreach (var path in _files.ClaimDue(_folders, maxCount, nowUtc, leaseUntilUtc))
        {
            var leased = _json.DeserializeReceived(File.ReadAllBytes(path))
                .ThenIfSuccess(message => ErrorOr<ReceivedMessage>.ForValue(Leased(message, leaseUntilUtc)));
            var content = leased.ThenIfSuccess(_json.Serialize);
            if (content.IsError)
            {
                SetAside(path, content.Error!);
                continue;
            }

            MessageFiles.Overwrite(path, content.Value);
            claimed.Add(leased.Value);
        }

        return new ClaimResult<ReceivedMessage>(claimed, MessageFiles.NextDueAtUtc(_folders));
    }

    private Error? Update(ReceivedMessage message, Func<DeliveryState, DeliveryState> change)
    {
        ArgumentNullException.ThrowIfNull(message);
        return FileFailures.Run(() =>
        {
            var path = MessageFiles.Find(RootOf(message.Flags), MessageFiles.KeyOf(message.Delivery.Id, message.Tenant));
            if (path is null)
            {
                return new Error(Errors.NotFound, "The received message is not stored.");
            }

            var changed = _json.DeserializeReceived(File.ReadAllBytes(path))
                .ThenIfSuccess(stored => ErrorOr<ReceivedMessage>.ForValue(stored with { Delivery = change(stored.Delivery) }));
            var content = changed.ThenIfSuccess(_json.Serialize);
            if (content.IsError)
            {
                return content.Error;
            }

            MessageFiles.Replace(path, changed.Value.Delivery, content.Value);
            return null;
        });
    }

    private Error? Add(TenantKey key, ReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Tenant != key.Tenant)
        {
            return new Error(Errors.ValidationFailed, "The message has another tenant than the key of the store.");
        }

        var content = _json.Serialize(message);
        if (content.IsError)
        {
            return content.Error;
        }

        var root = RootOf(message.Flags);
        return FileFailures.Run(() =>
        {
            _files.Add(
                MessageFiles.MarkerOf(root, message.Tenant, message.Delivery.Id), root, message.Delivery,
                MessageFiles.KeyOf(message.Delivery.Id, message.Tenant), content.Value);
            return null;
        });
    }

    private string RootOf(MessageFlags flags) => flags.HasFlag(MessageFlags.Test) ? _testRoot : _root;

    private static ReceivedMessage Leased(ReceivedMessage message, DateTime leaseUntilUtc) =>
        message with { Delivery = message.Delivery with { NextTryAtUtc = leaseUntilUtc, LeaseUntilUtc = leaseUntilUtc } };

    /// <summary>
    /// Keeps a file that the code cannot read out of the claims: a newer version stays claimed, so a newer instance
    /// reads it after the lease (section 4.13), and a permanent error moves it to <c>failed</c>.
    /// </summary>
    private static void SetAside(string path, Error error)
    {
        if (!error.Type.IsTransient)
        {
            MessageFiles.MoveToFailed(path);
        }
    }

    private sealed class TenantStore(FileSystemInboxStore store, TenantKey key) : IInboxStore
    {
        public ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, message));

        public ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(store.Query(key, query))));

        public ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => store.Get(key, id)));

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<IReadOnlyList<MessageCount>>.ForValue(store.Count(key, query))));

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<int>.ForValue(
                store.Change(key, ids, delivery => delivery.RetriedAt(nowUtc, addedTries)))));

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<int>.ForValue(store.Change(key, ids, delivery => delivery.CancelledAt(nowUtc)))));
    }
}
