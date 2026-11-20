using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

/// <summary>The selection of the delivery queries and commands in place of the in-process defaults of <c>AddMsgHatch</c> (D231, D289).</summary>
public sealed partial class MsgHatchBuilderUnitTests
{
    [Fact]
    public async Task UseDeliveryQueries_Selection_ReplacesTheDefaultForEachTenantKeyWithItsLoggingWrapper()
    {
        // Arrange
        var queries = Substitute.For<IDeliveryQueries>();
        queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelStatus>>.ForValue([])));
        var keys = new List<TenantKey>();
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseDeliveryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        });
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IDeliveryQueries>(TenantKey.None).GetChannelsAsync(Ct);
        await scope.ServiceProvider.GetRequiredKeyedService<IDeliveryQueries>(TenantKey.For(new TenantId("t1"))).GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(
            ("None,t1", 2, 2),
            (string.Join(',', keys.Select(static key => key.Tenant?.Value ?? "None")), queries.ReceivedCalls().Count(),
                provider.LogRecords().Count(static record => record.Id.Id == 1800)));
    }

    [Fact]
    public async Task UseDeliveryCommands_Selection_ReplacesTheDefaultWithItsLoggingWrapper()
    {
        // Arrange
        var commands = Substitute.For<IDeliveryCommands>();
        commands.ResumeChannelAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseDeliveryCommands((_, _) => commands);
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var error = await scope.ServiceProvider.GetRequiredService<IDeliveryCommands>().ResumeChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal(
            ((Error?)null, 1, 1),
            (error, commands.ReceivedCalls().Count(), provider.LogRecords().Count(static record => record.Id.Id == 1802)));
    }

    [Theory]
    [InlineData(nameof(IDeliveryQueries))]
    [InlineData(nameof(IDeliveryCommands))]
    public async Task UseDelivery_SecondSelection_NamesBothSetupMethods(string service)
    {
        // Arrange
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch();
        _ = service == nameof(IDeliveryQueries)
            ? builder.UseDeliveryQueries(static (_, _) => Substitute.For<IDeliveryQueries>(), "UseFirstDeliveryClient")
                .UseDeliveryQueries(static (_, _) => Substitute.For<IDeliveryQueries>())
            : builder.UseDeliveryCommands(static (_, _) => Substitute.For<IDeliveryCommands>(), "UseFirstDeliveryClient")
                .UseDeliveryCommands(static (_, _) => Substitute.For<IDeliveryCommands>());
        await using var provider = services.Build();
        var second = service == nameof(IDeliveryQueries) ? nameof(MsgHatchBuilder.UseDeliveryQueries) : nameof(MsgHatchBuilder.UseDeliveryCommands);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(
            $"UseFirstDeliveryClient and {second} select an implementation of {service}.",
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }
}
