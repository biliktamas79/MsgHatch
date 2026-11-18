using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

/// <summary>The selection of the registry queries in place of the in-process default of <c>AddMsgHatch</c> (D231, D269).</summary>
public sealed partial class MsgHatchBuilderUnitTests
{
    [Fact]
    public async Task UseRegistryQueries_Selection_ReplacesTheDefaultForEachTenantKeyWithItsLoggingWrapper()
    {
        // Arrange
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue([])));
        var keys = new List<TenantKey>();
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        });
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None).GetChannelsAsync(Ct);
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.For(new TenantId("t1"))).GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(
            ("None,t1", 2, 2),
            (string.Join(',', keys.Select(static key => key.Tenant?.Value ?? "None")), queries.ReceivedCalls().Count(),
                provider.LogRecords().Count(static record => record.Id.Id == 2909)));
    }

    [Fact]
    public async Task UseRegistryQueries_SecondSelection_NamesBothSetupMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .UseRegistryQueries(static (_, _) => Substitute.For<IRegistryQueries>(), "UseFirstRegistryClient")
            .UseRegistryQueries(static (_, _) => Substitute.For<IRegistryQueries>());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(
            "UseFirstRegistryClient and UseRegistryQueries select an implementation of IRegistryQueries.",
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }
}
