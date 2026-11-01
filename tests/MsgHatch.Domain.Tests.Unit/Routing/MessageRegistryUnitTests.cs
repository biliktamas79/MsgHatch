using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>The in-memory registry reader that the registrations fill (D117).</summary>
public sealed class MessageRegistryUnitTests
{
    private static readonly ChannelDescriptor<TestChannelSettings> _second = new(
        new ChannelId("Second"), "Second channel", ChannelFeatures.GroupDelivery);

    [Fact]
    public async Task Channels_TwoRegisteredChannels_GivesEachChannel()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();

        // Act
        var channels = provider.GetRequiredService<IMessageRegistry>().Channels;

        // Assert
        Assert.Equal([TestChannel.Descriptor, _second], channels);
    }

    [Fact]
    public async Task MessageTypes_TwoRegisteredMessageTypes_GivesEachMessageTypeWithItsChannels()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();

        // Act
        var messageTypes = provider.GetRequiredService<IMessageRegistry>().MessageTypes;

        // Assert
        Assert.Equal(
            ["TestEvent TestEvent [Test] True", "OtherTestEvent OtherTestEvent [Test,Second] True"],
            messageTypes.Select(static m =>
                $"{m.Type.Name} {m.EventType.Name} [{string.Join(',', m.Channels.Select(static c => c.Name))}] {m.Enabled}"));
    }

    [Fact]
    public async Task FindChannel_RegisteredChannel_GivesItsDescriptor()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(_second.Id);

        // Assert
        Assert.Same(_second, channel);
    }

    [Fact]
    public async Task FindMessageType_RegisteredMessageType_GivesItsDescriptor()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();

        // Act
        var messageType = provider.GetRequiredService<IMessageRegistry>().FindMessageType(new MessageType("OtherTestEvent"));

        // Assert
        Assert.Equal(typeof(OtherTestEvent), messageType?.EventType);
    }

    [Fact]
    public async Task FindChannel_UnknownChannel_GivesNull()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(new ChannelId("Unknown"));

        // Assert
        Assert.Null(channel);
    }

    [Fact]
    public async Task FindMessageType_UnknownMessageType_GivesNull()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();

        // Act
        var messageType = provider.GetRequiredService<IMessageRegistry>().FindMessageType(new MessageType("Unknown"));

        // Assert
        Assert.Null(messageType);
    }

    [Fact]
    public async Task Read_AnyLookup_AllocatesZeroBytes()
    {
        // Arrange
        await using var provider = BuildWithTwoChannelsAndTwoMessageTypes();
        var registry = provider.GetRequiredService<IMessageRegistry>();
        var channel = _second.Id;
        var type = new MessageType("OtherTestEvent");
        _ = (registry.FindChannel(channel), registry.FindMessageType(type), registry.Channels, registry.MessageTypes);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = registry.FindChannel(channel);
        _ = registry.FindMessageType(type);
        _ = registry.Channels;
        _ = registry.MessageTypes;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(0, allocated);
    }

    private static ServiceProvider BuildWithTwoChannelsAndTwoMessageTypes()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .AddTestChannel()
            .AddChannelType(_second, 1, TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>())
            .UseRouter(typeof(TestOpenRouter<>))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>())
                .AsChannel(_second.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>()));
        return services.Build();
    }
}
