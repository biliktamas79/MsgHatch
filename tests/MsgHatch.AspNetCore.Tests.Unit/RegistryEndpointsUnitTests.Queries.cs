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
        const string channel = "Sms";
        const string name = "Text messages";
        const string description = "Short texts";
        const int revision = 7;
        var keys = new List<TenantKey>();
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue(
            [new RegistryChannel(new ChannelId(channel), name, description, ChannelFeatures.Languages, Enabled: false, revision)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        }));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ($$"""[{"id":"{{channel}}","name":"{{name}}","description":"{{description}}","features":"Languages","enabled":false,"revision":{{revision}},"current":null,"entry":null}]""", 1, 0),
            (json, keys.Count(static key => key == TenantKey.None), host.Writer.ReceivedCalls().Count()));
        await queries.Received(1).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetChannels_DownChannel_WritesItsCurrentStateRow()
    {
        // Arrange
        const string channel = "Sms";
        const string name = "Text messages";
        const int version = 3;
        const int errorCode = 503;
        const string errorName = "Unavailable";
        var queries = Substitute.For<IRegistryQueries>();
        var current = new ChannelState(
            new ChannelId(channel), version, ChannelStateKind.Down, _sinceUtc, ChannelStateKind.Up, TimeSpan.FromMinutes(4), ChannelDownReason.Errors,
            _sinceUtc.AddSeconds(30), errorCode, errorName, null);
        queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryChannel>>.ForValue(
            [new RegistryChannel(current.Channel, name, null, ChannelFeatures.None, Enabled: true, Revision: 0, current)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, _) => queries));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            $$"""[{"id":"{{channel}}","name":"{{name}}","description":null,"features":"None","enabled":true,"revision":0,"current":{"channel":"{{channel}}","version":{{version}},"ki"""
            + """nd":"Down","sinceUtc":"2026-11-18T13:32:00Z","previousKind":"Up","previousDurationMs":240000,"reason":"Errors","nextCheck"""
            + $$"""AtUtc":"2026-11-18T13:32:30Z","errorCode":{{errorCode}},"errorName":"{{errorName}}","note":null},"entry":null}]""",
            json);
        await queries.Received(1).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetChannelHistory_SelectedQueries_GivesTheStateRowsOfTheChannelFromTheQueriesOfTenantKeyNone()
    {
        // Arrange
        const string channel = "Sms";
        const int version = 2;
        var keys = new List<TenantKey>();
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetChannelHistoryAsync(new ChannelId(channel), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForValue(
            [new ChannelState(new ChannelId(channel), version, ChannelStateKind.Up, _sinceUtc, ChannelStateKind.Down, TimeSpan.FromMinutes(90), null, null, null, null, null)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        }));

        // Act
        var json = await host.Client.GetStringAsync(new Uri($"/registry/channels/{channel}/history", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ($$"""[{"channel":"{{channel}}","version":{{version}},"kind":"Up","sinceUtc":"2026-11-18T13:32:00Z","previousKind":"Down","previousDurationMs":5400000,"reason":null,"next"""
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
        const string type = "Invoices";
        const string displayName = "Invoices";
        const string channel = "Sms";
        const int revision = 2;
        var keys = new List<TenantKey>();
        var queries = Substitute.For<IRegistryQueries>();
        queries.GetMessageTypesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<RegistryMessageType>>.ForValue(
            [new RegistryMessageType(new MessageType(type), displayName, null, [new ChannelId(channel)], Enabled: true, [new ChannelId(channel)], revision)])));
        await using var host = await RegistryHost.StartAsync(configure: builder => builder.UseRegistryQueries((_, key) =>
        {
            keys.Add(key);
            return queries;
        }));

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/message-types", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ($$"""[{"name":"{{type}}","displayName":"{{displayName}}","description":null,"channels":["{{channel}}"],"enabled":true,"disabledChannels":["{{channel}}"],"revision":{{revision}},"entry":null}]""", 1, 0),
            (json, keys.Count(static key => key == TenantKey.None), host.Writer.ReceivedCalls().Count()));
        await queries.Received(1).GetMessageTypesAsync(Arg.Any<CancellationToken>());
    }
}
