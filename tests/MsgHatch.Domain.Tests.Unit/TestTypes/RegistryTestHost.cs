using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the tests of the stored registry: a host with the message type <c>TestEvent</c> on the channels Test and
/// Third, the message type <c>OtherTestEvent</c> on the channel Test, and the channels Second and Described with no message type. Only the channel Described has a description in the code.
/// </summary>
public static class RegistryTestHost
{
    public const string RefreshInterval = "00:00:10";

    public static readonly MessageType TestEventType = new("TestEvent");

    public static readonly MessageType OtherEventType = new("OtherTestEvent");

    public static readonly ChannelDescriptor<TestChannelSettings> DescribedChannel =
        new(new ChannelId("Described"), "Described channel", ChannelFeatures.None, "Code description");

    public static ServiceProvider Build(IRegistrySettingsStore? store = null, TimeProvider? time = null, bool enableLogging = true)
    {
        var services = TestHost.CreateServices(time, new Dictionary<string, string?> { ["MsgHatch:Registry:RefreshInterval"] = RefreshInterval });
        var builder = services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .AddSecondTestChannel()
            .AddThirdTestChannel()
            .AddChannelType(DescribedChannel, 1, TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>())
            .UseRouter(TestOpenRouter.Factory)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>())
                .AsChannel(TestChannel.ThirdId, "OtherEnvelope", 1, TestJsonContext.Default.OtherTestEnvelope, AnyComposer<TestEvent, OtherTestEnvelope>()))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>()));
        _ = store is null ? builder.UseInMemoryStores() : builder.UseRegistrySettingsStore(_ => store);
        builder.UseStoredRegistry();
        return services.Build();
    }

    /// <summary>A store substitute that gives the reads in order, and succeeds each save and each delete.</summary>
    public static IRegistrySettingsStore AStoreThatReads(params RegistryEntries[] reads)
    {
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(default).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(reads[0])),
            [.. reads.Skip(1).Select(static read => ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(read)))]);
        store.SaveChannelEntryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        store.SaveMessageTypeEntryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        store.DeleteChannelEntryAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        store.DeleteMessageTypeEntryAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        return store;
    }

    /// <summary>A store substitute that gives the error for each member.</summary>
    public static IRegistrySettingsStore AStoreThatFails(Error error)
    {
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForError(error)));
        store.SaveChannelEntryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        store.SaveMessageTypeEntryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        store.DeleteChannelEntryAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        store.DeleteMessageTypeEntryAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        return store;
    }

    public static RegistryEntries NoEntries() => new([], []);

    public static ChannelEntry AChannelEntry(
        ChannelId? channel = null, string? displayName = "Shop channel", string? description = "Stored description", bool enabled = true,
        int revision = 0) =>
        new(channel ?? TestChannel.Id, displayName, description, enabled, revision, default);

    public static MessageTypeEntry AMessageTypeEntry(
        MessageType? type = null, IReadOnlyList<ChannelId>? disabledChannels = null, string? displayName = "Shop event",
        string? description = "Stored description", bool enabled = true, int revision = 0) =>
        new(type ?? TestEventType, displayName, description, enabled, disabledChannels ?? [], revision, default);

    private static Func<IServiceProvider, TenantKey, IEnvelopeComposer<TEvent, TEnvelope>> AnyComposer<TEvent, TEnvelope>() =>
        static (_, _) => Substitute.For<IEnvelopeComposer<TEvent, TEnvelope>>();
}
