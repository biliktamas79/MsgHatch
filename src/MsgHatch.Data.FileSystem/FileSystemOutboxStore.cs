using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The outbox in JSON files (section 4.5, D26): one file for each outgoing message, with its publish tries, under
/// <c>{RootPath}/outbox/{channel}</c>, and each test message under <c>{RootPath}/test/outbox/{channel}</c> (D104). A claim
/// moves a file from <c>pending</c> to <c>claimed</c> with an atomic rename. The down state of a channel is the file
/// <c>{RootPath}/outbox/{channel}/down.json</c> (D106). It is the dispatch store of all tenants, and <see cref="For"/>
/// gives the tenant store of one key.
/// </summary>
// ponytail: rename-based claims suit one machine or a shared folder with atomic rename, use a database store for more throughput
public sealed class FileSystemOutboxStore : IOutboxDispatchStore
{
    private const string _downFileName = "down.json";

    private readonly MsgHatchJson _json;
    private readonly MessageFiles _files = new();
    private readonly string _root;
    private readonly string _testRoot;
    private readonly string[] _roots;

    /// <summary>Makes the store on a folder.</summary>
    /// <param name="rootPath">The root folder of the file stores. The outbox is under <c>{rootPath}/outbox</c>.</param>
    /// <param name="json">The serializer of the messages.</param>
    public FileSystemOutboxStore(string rootPath, MsgHatchJson json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(json);
        RootPath = rootPath;
        _json = json;
        _root = Path.Combine(rootPath, "outbox");
        _testRoot = Path.Combine(rootPath, "test", "outbox");
        _roots = [_root, _testRoot];
    }

    /// <summary>The root folder of the file stores.</summary>
    public string RootPath { get; }

    /// <summary>Gives the tenant store of one key. It reads and writes only the messages of its tenant.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The tenant store.</returns>
    public IOutboxStore For(TenantKey key) => new TenantStore(this, key);

    /// <inheritdoc />
    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : FileFailures.Run(() => ReadDown(channel) is { } down
                ? new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null, down)
                : Claim(channel, maxCount, nowUtc, nowUtc + lease)));

    /// <inheritdoc />
    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publishTry);
        return ValueTask.FromResult(FileFailures.Run(() => RecordTry(message, publishTry, nextTryAtUtc)));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => ErrorOr<int>.ForValue(MessageFiles.DeleteRoot(_testRoot))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ValueTask.FromResult(FileFailures.Run(() => ErrorOr<IReadOnlyList<MessageCount>>.ForValue(Count(query, tenant: null))));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        ValueTask.FromResult(maxCount < 1
            ? new Error(Errors.ValidationFailed, "maxCount is less than 1.")
            : FileFailures.Run(() => ErrorOr<int>.ForValue(Purge(finishedBeforeUtc, maxCount))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => ErrorOr<IReadOnlyList<ChannelDown>>.ForValue(
        [
            .. ChannelFolders().Select(static folder => Path.Combine(folder, _downFileName)).Where(File.Exists).Select(ReadDownFile),
        ])));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(down);
        return ValueTask.FromResult(FileFailures.Run(() => ErrorOr<bool>.ForValue(
            down.Reason == ChannelDownReason.Errors ? TryCreateDown(down) : ReplaceDown(down))));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => ErrorOr<bool>.ForValue(EndDown(channel, reason))));

    /// <inheritdoc />
    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() => ErrorOr<bool>.ForValue(TryMoveNextCheck(channel, nowUtc, nextCheckAtUtc))));

    private ErrorOr<ClaimResult<OutgoingMessage>> Claim(ChannelId channel, int maxCount, DateTime nowUtc, DateTime leaseUntilUtc)
    {
        string[] folders = [Path.Combine(_root, channel.Name), Path.Combine(_testRoot, channel.Name)];
        var claimed = new List<OutgoingMessage>();
        foreach (var path in _files.ClaimDue(folders, maxCount, nowUtc, leaseUntilUtc))
        {
            var oldContent = File.ReadAllBytes(path);
            var leased = _json.DeserializeOutgoing(oldContent)
                .ThenIfSuccess(message => ErrorOr<OutgoingMessage>.ForValue(Leased(message, leaseUntilUtc)));
            var content = leased.ThenIfSuccess(message => OutboxFile.Write(_json, message, oldContent, newTry: null));
            if (content.IsError)
            {
                SetAside(path, content.Error!);
                continue;
            }

            MessageFiles.Overwrite(path, content.Value);
            claimed.Add(leased.Value);
        }

        return new ClaimResult<OutgoingMessage>(claimed, MessageFiles.NextDueAtUtc(folders));
    }

    private MessagePage<OutgoingMessageSummary> Query(TenantKey key, OutboxQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return MessagePage.Of(
            ReadEach(query.IncludeTest ? _roots : [_root], query.Status is { } status ? [status] : Enum.GetValues<DeliveryStatus>())
                .Select(static file => file.Message)
                .Where(message => message.Tenant == key.Tenant && query.Matches(message)),
            static message => message.Delivery, OutgoingMessageSummary.Of, query.After, query.Limit);
    }

    private ErrorOr<OutgoingMessageHistory?> Get(TenantKey key, Guid id)
    {
        if (PathOf(id) is not { } path)
        {
            return ErrorOr<OutgoingMessageHistory?>.ForValue(null);
        }

        var content = File.ReadAllBytes(path);
        var message = _json.DeserializeOutgoing(content);
        if (message.IsError)
        {
            return message.Error;
        }

        if (message.Value.Tenant != key.Tenant)
        {
            return ErrorOr<OutgoingMessageHistory?>.ForValue(null);
        }

        var tries = OutboxFile.ReadTries(id, content);
        return tries.IsError ? tries.Error : new OutgoingMessageHistory(message.Value, tries.Value);
    }

    /// <summary>
    /// Changes each readable message of the tenant with one of the ids, when the change gives a new message and maybe a
    /// new try. It moves the file into the folder of its new status.
    /// </summary>
    private int Change(TenantKey key, IReadOnlyList<Guid> ids, Func<OutgoingMessage, (OutgoingMessage Message, PublishTry? NewTry)?> change)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return _files.WhileNoClaimRuns(() =>
        {
            var changed = 0;
            foreach (var path in ids.Select(PathOf).OfType<string>())
            {
                var oldContent = File.ReadAllBytes(path);
                var stored = _json.DeserializeOutgoing(oldContent);
                if (stored.IsSuccess && stored.Value.Tenant == key.Tenant && change(stored.Value) is { } result
                    && OutboxFile.Write(_json, result.Message, oldContent, result.NewTry) is { IsSuccess: true } content)
                {
                    MessageFiles.Replace(path, result.Message.Delivery, content.Value);
                    changed++;
                }
            }

            return changed;
        });
    }

    private string? PathOf(Guid id) =>
        _roots
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(folder => MessageFiles.Find(folder, MessageFiles.KeyOf(id, tenant: null)))
            .FirstOrDefault(static path => path is not null);

    private static (OutgoingMessage Message, PublishTry? NewTry)? Retried(OutgoingMessage message, DateTime nowUtc, int addedTries) =>
        message.Delivery.RetriedAt(nowUtc, addedTries) is { } delivery ? (message with { Delivery = delivery }, null) : null;

    /// <summary>Cancels a message, and records the cancel as a publish try with the next try number (D110).</summary>
    private static (OutgoingMessage Message, PublishTry? NewTry)? Cancelled(OutgoingMessage message, DateTime nowUtc)
    {
        if (message.Delivery.CancelledAt(nowUtc) is not { } cancelled)
        {
            return null;
        }

        var tryNumber = message.Delivery.TryCount + 1;
        return (message with { Delivery = cancelled with { TryCount = tryNumber, LastTryAtUtc = nowUtc } },
            PublishTry.Cancel(message.Delivery.Id, tryNumber, nowUtc));
    }

    private IReadOnlyList<MessageCount> Count(TrafficQuery query, TenantKey? tenant) =>
        MessageCount.Of(
            ReadEach(query.IncludeTest ? _roots : [_root], query.Status is { } status ? [status] : Enum.GetValues<DeliveryStatus>())
                .Select(static file => file.Message)
                .Where(message => (tenant is null || message.Tenant == tenant.Value.Tenant) && query.Matches(message.Delivery, message.Flags))
                .Select(static message => (message.Type, (ChannelId?)message.Channel, message.Delivery, message.Flags)));

    private int Purge(DateTime finishedBeforeUtc, int maxCount)
    {
        (string Root, string Path, OutgoingMessage Message)[] purged =
        [
            .. ReadEach(_roots, MessageFiles.FinishedStatuses)
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

    /// <summary>Reads each readable message file of the given statuses in the channel folders of the roots (D167).</summary>
    private IEnumerable<(string Root, string Path, OutgoingMessage Message)> ReadEach(
        IEnumerable<string> roots, IEnumerable<DeliveryStatus> statuses) =>
        from root in roots
        from folder in Directory.Exists(root) ? Directory.EnumerateDirectories(root) : []
        from file in MessageFiles.ReadEach(MessageFiles.FilesOf(folder, statuses), content => _json.DeserializeOutgoing(content))
        select (root, file.Path, file.Message);

    private Error? RecordTry(OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc)
    {
        var path = MessageFiles.Find(Path.Combine(RootOf(message.Flags), message.Channel.Name), MessageFiles.KeyOf(message.Delivery.Id, tenant: null));
        if (path is null)
        {
            return new Error(Errors.NotFound, "The outgoing message is not stored.");
        }

        var oldContent = File.ReadAllBytes(path);
        var stored = _json.DeserializeOutgoing(oldContent);
        if (stored.IsError || stored.Value.Delivery.TryCount != publishTry.TryNumber - 1)
        {
            return stored.Error;
        }

        var changed = stored.Value with { Delivery = AfterTry(stored.Value.Delivery, publishTry, nextTryAtUtc) };
        var content = OutboxFile.Write(_json, changed, oldContent, publishTry);
        if (content.IsSuccess)
        {
            MessageFiles.Replace(path, changed.Delivery, content.Value);
        }

        return content.Error;
    }

    private Error? Add(TenantKey key, IReadOnlyList<OutgoingMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Any(message => message.Tenant != key.Tenant))
        {
            return new Error(Errors.ValidationFailed, "A message has another tenant than the key of the store.");
        }

        return FileFailures.Run(() =>
        {
            foreach (var message in messages)
            {
                var content = OutboxFile.Write(_json, message, oldContent: null, newTry: null);
                if (content.IsError)
                {
                    return content.Error;
                }

                var root = RootOf(message.Flags);
                _files.Add(
                    MessageFiles.MarkerOf(root, message.Tenant, message.Delivery.Id), Path.Combine(root, message.Channel.Name),
                    message.Delivery, MessageFiles.KeyOf(message.Delivery.Id, tenant: null), content.Value);
            }

            return null;
        });
    }

    private ChannelDown? ReadDown(ChannelId channel)
    {
        var path = DownPathOf(channel);
        return File.Exists(path) ? ReadDownFile(path) : null;
    }

    private bool TryCreateDown(ChannelDown down)
    {
        var path = DownPathOf(down.Channel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, down, FileStoreJsonContext.Default.ChannelDown);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }

    private bool ReplaceDown(ChannelDown down)
    {
        var path = DownPathOf(down.Channel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        stream.SetLength(0);
        JsonSerializer.Serialize(stream, down, FileStoreJsonContext.Default.ChannelDown);
        return true;
    }

    private bool EndDown(ChannelId channel, ChannelDownReason? reason)
    {
        var path = DownPathOf(channel);
        bool ends;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var down = JsonSerializer.Deserialize(stream, FileStoreJsonContext.Default.ChannelDown);
            ends = down is not null && (reason is null || down.Reason == reason);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        if (ends)
        {
            File.Delete(path);
        }

        return ends;
    }

    /// <summary>
    /// Moves the next check time while the file is open with <see cref="FileShare.None"/>, so of two parallel callers
    /// only one can start the check. A caller that cannot open the file does not start the check.
    /// </summary>
    private bool TryMoveNextCheck(ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(DownPathOf(channel), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return false;
        }

        using (stream)
        {
            var down = JsonSerializer.Deserialize(stream, FileStoreJsonContext.Default.ChannelDown);
            var due = down is { Reason: ChannelDownReason.Errors } && down.NextCheckAtUtc <= nowUtc;
            if (due)
            {
                stream.SetLength(0);
                stream.Position = 0;
                JsonSerializer.Serialize(stream, down! with { NextCheckAtUtc = nextCheckAtUtc }, FileStoreJsonContext.Default.ChannelDown);
            }

            return due;
        }
    }

    private IEnumerable<string> ChannelFolders() => Directory.Exists(_root) ? Directory.EnumerateDirectories(_root) : [];

    private string DownPathOf(ChannelId channel) => Path.Combine(_root, channel.Name, _downFileName);

    private string RootOf(MessageFlags flags) => flags.HasFlag(MessageFlags.Test) ? _testRoot : _root;

    private static ChannelDown ReadDownFile(string path) =>
        JsonSerializer.Deserialize(File.ReadAllBytes(path), FileStoreJsonContext.Default.ChannelDown)
        ?? throw new JsonException($"The down state file {path} holds null.");

    private static OutgoingMessage Leased(OutgoingMessage message, DateTime leaseUntilUtc) =>
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

    private static DeliveryState AfterTry(DeliveryState delivery, PublishTry publishTry, DateTime? nextTryAtUtc)
    {
        var status = publishTry.StatusAfter(nextTryAtUtc);
        return delivery with
        {
            Status = status,
            TryCount = publishTry.TryNumber,
            LastTryAtUtc = publishTry.FinishedAtUtc,
            NextTryAtUtc = status == DeliveryStatus.Pending ? nextTryAtUtc!.Value : delivery.NextTryAtUtc,
            LeaseUntilUtc = null,
            LastError = publishTry.ErrorText(),
        };
    }

    private sealed class TenantStore(FileSystemOutboxStore store, TenantKey key) : IOutboxStore
    {
        public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) =>
            ValueTask.FromResult(store.Add(key, messages));

        public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(store.Query(key, query))));

        public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => store.Get(key, id)));

        public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(query);
            return ValueTask.FromResult(FileFailures.Run(() => ErrorOr<IReadOnlyList<MessageCount>>.ForValue(store.Count(query, key))));
        }

        public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<int>.ForValue(
                store.Change(key, ids, message => Retried(message, nowUtc, addedTries)))));

        public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
            ValueTask.FromResult(FileFailures.Run(() => ErrorOr<int>.ForValue(store.Change(key, ids, message => Cancelled(message, nowUtc)))));
    }
}
