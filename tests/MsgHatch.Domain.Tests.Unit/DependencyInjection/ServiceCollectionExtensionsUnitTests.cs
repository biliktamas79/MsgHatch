using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
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
}
