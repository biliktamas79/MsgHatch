using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The registry settings on EF Core (section 4.20): one row of <c>MsgHatchChannelEntry</c> for each channel entry, and
/// one row of <c>MsgHatchMessageTypeEntry</c> for each message type entry, with <c>DisabledChannels</c> as a JSON array.
/// A new entry is an insert, and a key that is already stored gives <see cref="Errors.Conflict"/>. A change is an
/// update with the expected revision, and a changed row count of 0 gives <see cref="Errors.Conflict"/>.
/// </summary>
public sealed class EntityFrameworkRegistrySettingsStore : IRegistrySettingsStore
{
    private readonly StoreContexts<MsgHatchDbContext> _contexts;

    /// <summary>Makes the store on the contexts of a factory.</summary>
    /// <param name="contexts">The pooled factory of the contexts.</param>
    public EntityFrameworkRegistrySettingsStore(IDbContextFactory<MsgHatchDbContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        _contexts = new StoreContexts<MsgHatchDbContext>(contexts);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct) =>
        _contexts.RunAsync<RegistryEntries>(
            async context =>
            {
                var channels = await context.ChannelEntries.AsNoTracking().ToListAsync(ct);
                var messageTypes = await context.MessageTypeEntries.AsNoTracking().ToListAsync(ct);
                return new RegistryEntries(
                    [.. channels.OrderBy(static row => row.Channel, StringComparer.Ordinal).Select(ChannelEntryOf)],
                    [.. messageTypes.OrderBy(static row => row.Type, StringComparer.Ordinal).Select(MessageTypeEntryOf)]);
            },
            ct);

    /// <inheritdoc />
    public ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var name = entry.Channel.Name;
        return entry.Revision == 1
            ? InsertAsync(
                new ChannelEntryRow
                {
                    Channel = name, DisplayName = entry.DisplayName, Description = entry.Description, Enabled = entry.Enabled,
                    Revision = entry.Revision, LastModifiedAtUtc = entry.LastModifiedAtUtc,
                },
                context => context.ChannelEntries.AnyAsync(row => row.Channel == name, ct),
                ct)
            : UpdateAsync(context => context.ChannelEntries.Where(row => row.Channel == name && row.Revision == entry.Revision - 1).ExecuteUpdateAsync(
                set => set
                    .SetProperty(static row => row.DisplayName, entry.DisplayName)
                    .SetProperty(static row => row.Description, entry.Description)
                    .SetProperty(static row => row.Enabled, entry.Enabled)
                    .SetProperty(static row => row.Revision, entry.Revision)
                    .SetProperty(static row => row.LastModifiedAtUtc, entry.LastModifiedAtUtc),
                ct), ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var name = entry.Type.Name;
        var disabledChannels = ChannelNamesJson(entry.DisabledChannels);
        return entry.Revision == 1
            ? InsertAsync(
                new MessageTypeEntryRow
                {
                    Type = name, DisplayName = entry.DisplayName, Description = entry.Description, Enabled = entry.Enabled,
                    DisabledChannels = disabledChannels, Revision = entry.Revision, LastModifiedAtUtc = entry.LastModifiedAtUtc,
                },
                context => context.MessageTypeEntries.AnyAsync(row => row.Type == name, ct),
                ct)
            : UpdateAsync(context => context.MessageTypeEntries.Where(row => row.Type == name && row.Revision == entry.Revision - 1).ExecuteUpdateAsync(
                set => set
                    .SetProperty(static row => row.DisplayName, entry.DisplayName)
                    .SetProperty(static row => row.Description, entry.Description)
                    .SetProperty(static row => row.Enabled, entry.Enabled)
                    .SetProperty(static row => row.DisabledChannels, disabledChannels)
                    .SetProperty(static row => row.Revision, entry.Revision)
                    .SetProperty(static row => row.LastModifiedAtUtc, entry.LastModifiedAtUtc),
                ct), ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct) =>
        _contexts.RunAsync(
            async context =>
            {
                await context.ChannelEntries.Where(row => row.Channel == channel.Name).ExecuteDeleteAsync(ct);
                return (Error?)null;
            },
            ct);

    /// <inheritdoc />
    public ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct) =>
        _contexts.RunAsync(
            async context =>
            {
                await context.MessageTypeEntries.Where(row => row.Type == type.Name).ExecuteDeleteAsync(ct);
                return (Error?)null;
            },
            ct);

    /// <summary>
    /// Inserts the row of a new entry. A failed insert of a key that is stored gives <see cref="Errors.Conflict"/>, so of
    /// two writers only one creates the entry. Each other failed insert gives <see cref="Errors.Unavailable"/>.
    /// </summary>
    private ValueTask<Error?> InsertAsync<TRow>(TRow row, Func<MsgHatchDbContext, Task<bool>> isStored, CancellationToken ct)
        where TRow : class =>
        _contexts.RunAsync(
            async context =>
            {
                context.Add(row);
                try
                {
                    await context.SaveChangesAsync(ct);
                    return null;
                }
                catch (DbUpdateException)
                {
                    if (await isStored(context))
                    {
                        return StoredRevisionIsNotThePrevious();
                    }

                    throw;
                }
            },
            ct);

    private ValueTask<Error?> UpdateAsync(Func<MsgHatchDbContext, Task<int>> update, CancellationToken ct) =>
        _contexts.RunAsync(async context => await update(context) == 1 ? null : StoredRevisionIsNotThePrevious(), ct);

    private static Error StoredRevisionIsNotThePrevious() =>
        new(Errors.Conflict, "The stored revision is not the revision before the revision of the entry.");

    /// <exception cref="ArgumentException">The row has an invalid channel name.</exception>
    private static ChannelEntry ChannelEntryOf(ChannelEntryRow row) =>
        new(new ChannelId(row.Channel), row.DisplayName, row.Description, row.Enabled, row.Revision, row.LastModifiedAtUtc);

    /// <exception cref="ArgumentException">The row has an invalid name.</exception>
    /// <exception cref="JsonException">The disabled channels are not a JSON array of names.</exception>
    private static MessageTypeEntry MessageTypeEntryOf(MessageTypeEntryRow row) =>
        new(new MessageType(row.Type), row.DisplayName, row.Description, row.Enabled, ChannelsOf(row.DisabledChannels), row.Revision,
            row.LastModifiedAtUtc);

    /// <summary>The channel names as a JSON array. A channel name has only ASCII letters, digits, and <c>_</c>, so it has no character to escape.</summary>
    private static string ChannelNamesJson(IReadOnlyList<ChannelId> channels) =>
        $"[{string.Join(',', channels.Select(static channel => $"\"{channel.Name}\""))}]";

    private static ChannelId[] ChannelsOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(static element => new ChannelId(element.GetString()!))];
    }
}
