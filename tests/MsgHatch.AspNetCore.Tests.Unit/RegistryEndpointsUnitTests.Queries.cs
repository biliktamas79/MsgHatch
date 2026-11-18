using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The GET routes of the registry through <see cref="IRegistryQueries"/> of <see cref="TenantKey.None"/> (D270).</summary>
public sealed partial class RegistryEndpointsUnitTests
{
    [Fact]
    public async Task GetChannels_SelectedQueries_GivesTheChannelsOfTheQueriesOfTenantKeyNoneAndCallsNoWriter()
    {
        // Arrange
        var keys = new List<TenantKey>();
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue(
            [new RegistryChannel(new ChannelId("Sms"), "Text messages", "Short texts", ChannelFeatures.Languages, Enabled: false, Revision: 7)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        }));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ("""[{"id":"Sms","name":"Text messages","description":"Short texts","features":"Languages","enabled":false,"revision":7}]""", 1, 0),
            (json, keys.Count(static key => key == TenantKey.None), host.Writer.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task GetMessageTypes_SelectedQueries_GivesTheMessageTypesOfTheQueriesOfTenantKeyNoneAndCallsNoWriter()
    {
        // Arrange
        var keys = new List<TenantKey>();
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetMessageTypesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryMessageType>>.ForValue(
            [new RegistryMessageType(new MessageType("Invoices"), "Invoices", null, [new ChannelId("Sms")], Enabled: true, [new ChannelId("Sms")], Revision: 2)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        }));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/message-types", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ("""[{"name":"Invoices","displayName":"Invoices","description":null,"channels":["Sms"],"enabled":true,"disabledChannels":["Sms"],"revision":2}]""", 1, 0),
            (json, keys.Count(static key => key == TenantKey.None), host.Writer.ReceivedCalls().Count()));
    }
}
