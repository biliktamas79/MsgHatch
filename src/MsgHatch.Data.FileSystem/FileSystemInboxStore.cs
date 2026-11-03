using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
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
                MessageFiles.MarkerOf(root, message.Tenant, message.Delivery.Id), root, message.Delivery.NextTryAtUtc,
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
    }
}
