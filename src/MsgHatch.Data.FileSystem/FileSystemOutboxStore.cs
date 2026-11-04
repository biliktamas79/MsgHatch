using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
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
                    message.Delivery.NextTryAtUtc, MessageFiles.KeyOf(message.Delivery.Id, tenant: null), content.Value);
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
    }
}
