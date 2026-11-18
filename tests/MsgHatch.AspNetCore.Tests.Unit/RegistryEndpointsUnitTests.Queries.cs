using System.Net;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The GET routes of the registry through <see cref="IRegistryQueries"/> of <see cref="TenantKey.None"/> (D270).</summary>
public sealed partial class RegistryEndpointsUnitTests
{
    private static readonly DateTime _sinceUtc = new(2026, 11, 18, 13, 32, 0, DateTimeKind.Utc);

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
            ("""[{"id":"Sms","name":"Text messages","description":"Short texts","features":"Languages","enabled":false,"revision":7,"current":null}]""", 1, 0),
            (json, keys.Count(static key => key == TenantKey.None), host.Writer.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task GetChannels_DownChannel_WritesItsCurrentStateRow()
    {
        // Arrange
        var queries = Substitute.For<IRegistryQueries>();
        var current = new ChannelState(
            new ChannelId("Sms"), 3, ChannelStateKind.Down, _sinceUtc, ChannelStateKind.Up, TimeSpan.FromMinutes(4), ChannelDownReason.Errors,
            _sinceUtc.AddSeconds(30), 503, "Unavailable", null);
        queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue(
            [new RegistryChannel(current.Channel, "Text messages", null, ChannelFeatures.None, Enabled: true, Revision: 0, current)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, _) => queries));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            """[{"id":"Sms","name":"Text messages","description":null,"features":"None","enabled":true,"revision":0,"current":{"channel":"Sms","version":3,"ki"""
            + """nd":"Down","sinceUtc":"2026-11-18T13:32:00Z","previousKind":"Up","previousDurationMs":240000,"reason":"Errors","nextCheck"""
            + """AtUtc":"2026-11-18T13:32:30Z","errorCode":503,"errorName":"Unavailable","note":null}}]""",
            json);
    }

    [Fact]
    public async Task GetChannelHistory_SelectedQueries_GivesTheStateRowsOfTheChannelFromTheQueriesOfTenantKeyNone()
    {
        // Arrange
        var keys = new List<TenantKey>();
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetChannelHistoryAsync(new ChannelId("Sms"), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForValue(
            [new ChannelState(new ChannelId("Sms"), 2, ChannelStateKind.Up, _sinceUtc, ChannelStateKind.Down, TimeSpan.FromMinutes(90), null, null, null, null, null)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        }));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/channels/Sms/history", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ("""[{"channel":"Sms","version":2,"kind":"Up","sinceUtc":"2026-11-18T13:32:00Z","previousKind":"Down","previousDurationMs":5400000,"reason":null,"next"""
                + """CheckAtUtc":null,"errorCode":null,"errorName":null,"note":null}]""", 1),
            (json, keys.Count(static key => key == TenantKey.None)));
    }

    [Theory]
    [InlineData("not-a-name", HttpStatusCode.BadRequest)]
    [InlineData("Unknown", HttpStatusCode.NotFound)]
    public async Task GetChannelHistory_ChannelThatIsNotValidOrNotRegistered_GivesItsStatus(string channel, HttpStatusCode expected)
    {
        // Arrange
        await using var host = await RegistryHost.StartAsync();

        // Act
        var response = await host.Client.GetAsync(new Uri($"/registry/channels/{channel}/history", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(expected, response.StatusCode);
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
