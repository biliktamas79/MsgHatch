using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The test channel: its id, its descriptor, and its registration through the public builder.</summary>
public static class TestChannel
{
    public static readonly ChannelId Id = new("Test");

    public static readonly ChannelDescriptor<TestChannelSettings> Descriptor = new(Id, "Test channel", ChannelFeatures.None);

    public static readonly ChannelId SecondId = new("Second");

    public static readonly ChannelDescriptor<OtherTestChannelSettings> SecondDescriptor =
        new(SecondId, "Second test channel", ChannelFeatures.GroupDelivery);

    public static readonly ChannelId ThirdId = new("Third");

    public static readonly ChannelDescriptor<TestChannelSettings> ThirdDescriptor = new(ThirdId, "Third test channel", ChannelFeatures.None);

    public static Func<IServiceProvider, TenantKey, IEnvelopeComposer<TEvent, TestEnvelope>> AnyComposer<TEvent>() =>
        static (_, _) => Substitute.For<IEnvelopeComposer<TEvent, TestEnvelope>>();

    public static MsgHatchBuilder AddTestChannel(this MsgHatchBuilder builder) =>
        builder.AddChannelType(Descriptor, 1, TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>());

    public static MsgHatchBuilder AddSecondTestChannel(this MsgHatchBuilder builder) =>
        builder.AddChannelType(
            SecondDescriptor, 1, TestJsonContext.Default.OtherTestChannelSettings, Substitute.For<IChannelSettingsValidator>());

    public static MsgHatchBuilder AddThirdTestChannel(this MsgHatchBuilder builder) =>
        builder.AddChannelType(ThirdDescriptor, 1, TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>());
}
