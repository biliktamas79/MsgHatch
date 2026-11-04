using System.Text.Json;
using MsgHatch.Data.FileSystem;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Data.FileSystem;

/// <summary>
/// The subscription store in JSON files (section 4.5, D78, D97): the file
/// <c>{RootPath}/subscriptions/{tenantFolder}/{MessageType}/{level}/{ownerId}/{id}.json</c> for each subscription. The
/// store of one tenant key reads and writes only the folder of its tenant. A file lock on the owner folder serializes the
/// changes of one owner, and a change while another caller holds that lock gives <see cref="Errors.Unavailable"/>. It is
/// thread-safe.
/// </summary>
// ponytail: one file for each subscription and a folder lock suit small subscription sets, use a database store for many owners
public sealed class FileSystemSubscriptionStore : ISubscriptionStore
{
    private const string _jsonPattern = "*.json";
    private const string _lockFile = ".lock";

    private readonly TenantId? _tenant;
    private readonly string _tenantFolder;
    private readonly ChannelSettingsJson _settingsJson;

    /// <summary>Makes the store of one tenant key on a root folder.</summary>
    /// <param name="rootPath">The root folder of the file stores. The subscriptions are under <c>{RootPath}/subscriptions</c>.</param>
    /// <param name="key">The tenant key of the store.</param>
    /// <param name="settingsJson">The JSON of the channel settings.</param>
    public FileSystemSubscriptionStore(string rootPath, TenantKey key, ChannelSettingsJson settingsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(settingsJson);
        RootPath = rootPath;
        _tenant = key.Tenant;
        _tenantFolder = key.Tenant is { } tenant
            ? Path.Combine(rootPath, "subscriptions", "tenants", FileNames.Escape(tenant.Value))
            : Path.Combine(rootPath, "subscriptions", "no-tenant");
        _settingsJson = settingsJson;
    }

    /// <summary>The root folder of the file stores.</summary>
    public string RootPath { get; }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(users);
        IEnumerable<SubscriptionOwner> owners =
        [
            SubscriptionOwner.Default,
            .. companies.Select(SubscriptionOwner.ForCompany),
            .. users.Select(SubscriptionOwner.ForUser),
        ];
        return Read(owners.Distinct().SelectMany(owner => FilesOf(OwnerFolder(type, owner))), static _ => true);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Read(
            FilesOf(_tenantFolder, SearchOption.AllDirectories),
            subscription =>
                (query.Type is null || subscription.Type == query.Type)
                && (query.Company is null || subscription.Owner.Company == query.Company)
                && (query.User is null || subscription.Owner.User == query.User)
                && (query.Level is null || subscription.Owner.Level == query.Level));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run<Subscription?>(() =>
        {
            if (FileOf(id) is not { } path)
            {
                return ErrorOr<Subscription?>.ForValue(null);
            }

            var subscription = ReadFile(path);
            return subscription.IsError ? subscription.Error! : subscription.Value;
        }));

    /// <inheritdoc />
    public ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return ValueTask.FromResult(ChangeOwner(subscription, () =>
            HasSameOwnerTypeChannelAndRecipients(subscription) ? SameOwnerTypeChannelAndRecipients() : Write(subscription)));
    }

    /// <inheritdoc />
    public ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return ValueTask.FromResult(ChangeOwner(subscription, () => FileOf(subscription.Id) is { } path
            ? Replace(path, subscription, expectedRevision)
            : new Error(Errors.NotFound, "The subscription is not stored.")));
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct) =>
        ValueTask.FromResult(FileFailures.Run(() =>
        {
            if (FileOf(id) is { } path)
            {
                using var ownerLock = LockFolder(Path.GetDirectoryName(path)!);
                File.Delete(path);
            }

            return null;
        }));

    private Error? Replace(string path, Subscription subscription, int expectedRevision)
    {
        var stored = ReadFile(path);
        Error? error =
            stored.IsError ? stored.Error
            : stored.Value.Revision != expectedRevision ? new Error(Errors.Conflict, "The stored revision is not the expected revision.")
            : HasSameOwnerTypeChannelAndRecipients(subscription) ? SameOwnerTypeChannelAndRecipients()
            : Write(subscription);
        if (error is null && !string.Equals(path, PathOf(subscription), StringComparison.Ordinal))
        {
            File.Delete(path);
        }

        return error;
    }

    private Error? ChangeOwner(Subscription subscription, Func<Error?> change) =>
        FileFailures.Run(() =>
        {
            using var ownerLock = LockFolder(OwnerFolder(subscription.Type, subscription.Owner));
            return change();
        });

    private ValueTask<ErrorOr<IReadOnlyList<Subscription>>> Read(IEnumerable<string> files, Func<Subscription, bool> match) =>
        ValueTask.FromResult(FileFailures.Run<IReadOnlyList<Subscription>>(() =>
        {
            var subscriptions = new List<Subscription>();
            foreach (var path in files)
            {
                var subscription = ReadFile(path);
                if (subscription.IsError)
                {
                    return subscription.Error!;
                }

                if (match(subscription.Value))
                {
                    subscriptions.Add(subscription.Value);
                }
            }

            return subscriptions;
        }));

    private ErrorOr<Subscription> ReadFile(string path)
    {
        try
        {
            return ReadDocument(path).ToSubscription(_settingsJson);
        }
        catch (ArgumentException exception)
        {
            return new Error(Errors.SerializationFailed, exception.Message, exception);
        }
    }

    private Error? Write(Subscription subscription)
    {
        var file = SubscriptionFile.From(subscription, _tenant, _settingsJson);
        if (file.IsError)
        {
            return file.Error;
        }

        MessageFiles.Overwrite(PathOf(subscription), JsonSerializer.SerializeToUtf8Bytes(file.Value, SubscriptionFileJson.Default.SubscriptionFile));
        return null;
    }

    private bool HasSameOwnerTypeChannelAndRecipients(Subscription subscription) =>
        FilesOf(OwnerFolder(subscription.Type, subscription.Owner))
            .Select(ReadDocument)
            .Any(stored =>
                stored.Id != subscription.Id
                && string.Equals(stored.Channel, subscription.Channel?.Name, StringComparison.Ordinal)
                && stored.Recipients.Kind == subscription.Recipients.Kind);

    private static SubscriptionFile ReadDocument(string path) =>
        JsonSerializer.Deserialize(File.ReadAllBytes(path), SubscriptionFileJson.Default.SubscriptionFile)
        ?? throw new JsonException("A subscription file holds null.");

    private static Error SameOwnerTypeChannelAndRecipients() =>
        new(Errors.Conflict, "The owner already has a subscription for the message type, the channel, and the recipient kind.");

    private string? FileOf(Guid id) => FilesOf(_tenantFolder, SearchOption.AllDirectories, $"{id:D}.json").FirstOrDefault();

    private string PathOf(Subscription subscription) =>
        Path.Combine(OwnerFolder(subscription.Type, subscription.Owner), $"{subscription.Id:D}.json");

    private string OwnerFolder(MessageType type, SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => Path.Combine(_tenantFolder, type.Name, "default", "default"),
        SubscriptionLevel.Company => Path.Combine(_tenantFolder, type.Name, "company", FileNames.Escape(owner.Company!.Value.Value)),
        _ => Path.Combine(_tenantFolder, type.Name, "user", FileNames.Escape(owner.User!.Value.Value)),
    };

    private static IEnumerable<string> FilesOf(string folder, SearchOption search = SearchOption.TopDirectoryOnly, string pattern = _jsonPattern) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, pattern, search) : [];

    private static FileStream LockFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        return new FileStream(Path.Combine(folder, _lockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
