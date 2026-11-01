using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

public sealed class ServiceCollectionExtensionsUnitTests
{
    [Fact]
    public async Task AddMsgHatch_SecondCall_KeepsTheRegistrationsOfTheFirstCall()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel();
        services.AddMsgHatch().AddSecondTestChannel();

        // Act
        await using var provider = services.Build();

        // Assert
        Assert.Equal([TestChannel.Descriptor, TestChannel.SecondDescriptor], provider.GetRequiredService<IMessageRegistry>().Channels);
    }

    [Fact]
    public async Task AddMsgHatch_NoSignalSelection_RegistersTheInProcessDispatchSignalAsASingleton()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch();
        await using var provider = services.Build();

        // Act
        var signal = provider.GetRequiredService<IDispatchSignal>();

        // Assert
        Assert.IsType<InProcessDispatchSignal>(signal);
        Assert.Same(signal, provider.GetRequiredService<IDispatchSignal>());
    }

    [Fact]
    public async Task UseDispatchSignal_SignalType_ReplacesTheInProcessDispatchSignal()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseDispatchSignal<OtherDispatchSignal>();
        await using var provider = services.Build();

        // Act
        var signal = provider.GetRequiredService<IDispatchSignal>();

        // Assert
        Assert.IsType<OtherDispatchSignal>(signal);
    }

    private sealed class OtherDispatchSignal : IDispatchSignal
    {
        public ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct) => ValueTask.FromResult<Error?>(null);

        public ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct) => ValueTask.FromResult<Error?>(null);
    }
}
