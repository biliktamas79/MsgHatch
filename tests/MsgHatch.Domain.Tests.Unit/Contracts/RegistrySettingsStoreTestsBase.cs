using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IRegistrySettingsStore"/> (section 5.5). Each implementation gets them with one subclass
/// that only builds the store and its test data abstraction.
/// </summary>
/// <typeparam name="TStore">The store.</typeparam>
public abstract partial class RegistrySettingsStoreTestsBase<TStore> : IAsyncLifetime
    where TStore : class, IRegistrySettingsStore
{
    protected static readonly ChannelId Email = new("Email");
    protected static readonly ChannelId SignalR = new("SignalR");
    protected static readonly ChannelId Broker = new("Broker");
    protected static readonly MessageType TypeA = new("A");
    protected static readonly MessageType TypeB = new("B");

    protected RegistrySettingsStoreTestsBase(TStore store, Func<TStore, IRegistrySettingsStoreTestData> testData)
    {
        Store = store;
        TestData = testData(store);
    }

    protected TStore Store { get; }

    protected IRegistrySettingsStoreTestData TestData { get; }

    protected FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected DateTime Now => Time.GetUtcNow().UtcDateTime;

    public ValueTask InitializeAsync() => TestData.ResetAsync(Ct);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.ResetAsync(Ct);
    }

    protected ChannelEntry AChannelEntry(
        ChannelId? channel = null, int revision = 1, string? displayName = "Email of the shop", string? description = null,
        bool enabled = true) =>
        new(channel ?? Email, displayName, description, enabled, revision, Now);

    protected MessageTypeEntry AMessageTypeEntry(
        MessageType? type = null, int revision = 1, IReadOnlyList<ChannelId>? disabledChannels = null,
        string? displayName = "Order shipped", string? description = null, bool enabled = true) =>
        new(type ?? TypeA, displayName, description, enabled, disabledChannels ?? [], revision, Now);

    /// <summary>The fields of a message type entry, with the disabled channels as text, because a list compares by reference.</summary>
    protected static (MessageType, string?, string?, bool, string, int, DateTime) FieldsOf(MessageTypeEntry entry) =>
        (entry.Type, entry.DisplayName, entry.Description, entry.Enabled, string.Join(',', entry.DisabledChannels.Select(static channel => channel.Name)),
            entry.Revision, entry.LastModifiedAtUtc);

    protected async Task<RegistryEntries> EntriesAsync() => (await Store.GetEntriesAsync(Ct)).Value;
}
