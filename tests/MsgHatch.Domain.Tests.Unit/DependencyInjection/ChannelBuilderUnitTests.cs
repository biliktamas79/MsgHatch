using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class ChannelBuilderUnitTests
{
    [Fact]
    public async Task WithUpgrade_OldEnvelopeVersion_AddsTheContentTypeOfTheOldVersionWithItsType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
            .AsChannel(
                TestChannel.Id, "TestEnvelope", 2, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(), null,
                static channel => channel.WithUpgrade(1, TestJsonContext.Default.OtherTestEnvelope, static old => new TestEnvelope(old.Text))));

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IContentTypeRegistry>().TryFind("Test:TestEnvelope", 1, out var entry));
        Assert.Equal(typeof(OutgoingMessage<OtherTestEnvelope>), entry.ClrType);
    }

    [Fact]
    public void WithUpgrade_NullUpgrade_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch().AddTestChannel();

        // Act
        var exception = Record.Exception(() => builder.AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
            .AsChannel(
                TestChannel.Id, "TestEnvelope", 2, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(), null,
                static channel => channel.WithUpgrade<OtherTestEnvelope>(1, TestJsonContext.Default.OtherTestEnvelope, null!)))); // no upgrade

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }
}
