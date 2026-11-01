using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class MsgHatchBuilderUnitTests
{
    public static TheoryData<Type> TypesThatAreNotOpenGenericRouters =>
    [
        typeof(TestOpenRouter<TestEvent>),
        typeof(string),
        typeof(List<>),
        typeof(ListTestOpenRouter<>),
        typeof(FixedEventTestOpenRouter<>),
    ];

    [Fact]
    public async Task AddChannelType_NewChannel_AddsTheChannelAndItsSettingsContentType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Same(TestChannel.Descriptor, provider.GetRequiredService<IMessageRegistry>().FindChannel(TestChannel.Id));
        Assert.True(provider.GetRequiredService<IContentTypeRegistry>().TryFind("Settings:Test", 1, out var entry));
        Assert.Equal(("Settings:Test@1", typeof(TestChannelSettings)), (entry.Discriminator, entry.ClrType));
    }

    [Fact]
    public async Task AddChannelType_SecondCallForTheSameChannel_ChangesNothing()
    {
        // Arrange
        var services = TestHost.CreateServices();
        var second = TestChannel.Descriptor with { Name = "Second" };
        services.AddMsgHatch()
            .AddTestChannel()
            .AddChannelType(second, 2, TestJsonContext.Default.TestChannelSettings, Substitute.For<IChannelSettingsValidator>());

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal([TestChannel.Descriptor], provider.GetRequiredService<IMessageRegistry>().Channels);
        Assert.Equal(
            ["Settings:Test@1"],
            provider.GetRequiredService<IContentTypeRegistry>().Entries.Select(static e => e.Discriminator));
    }

    [Fact]
    public async Task AddTraceType_TraceType_AddsTheTraceContentType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTraceType("Test", 1, TestJsonContext.Default.TestTrace);

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.True(provider.GetRequiredService<IContentTypeRegistry>().TryFind("Trace:Test", 1, out var entry));
        Assert.Equal(("Trace:Test@1", typeof(TestTrace)), (entry.Discriminator, entry.ClrType));
    }

    [Fact]
    public void AddTraceType_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.AddTraceType("Sub-scription", 1, TestJsonContext.Default.TestTrace));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public void AddMessage_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() =>
            builder.AddMessage("Test Event", 1, TestJsonContext.Default.TestEvent, static _ => { }));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Fact]
    public void AsChannel_InvalidName_ThrowsArgumentException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch().AddTestChannel();

        // Act
        var exception = Record.Exception(() =>
            builder.AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m.AsChannel(
                TestChannel.Id, "Test:Envelope", 1, TestJsonContext.Default.TestEnvelope,
                static (_, _) => Substitute.For<IEnvelopeComposer<TestEvent, TestEnvelope>>())));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }

    [Theory]
    [MemberData(nameof(TypesThatAreNotOpenGenericRouters))]
    public void UseRouter_TypeThatIsNotAnOpenGenericRouter_ThrowsArgumentException(Type routerType)
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.UseRouter(routerType));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }
}
