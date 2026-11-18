using System.Net;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The channel check route of the registry (D283), which calls <see cref="IRegistryCommands"/> of <c>TenantKey.None</c>.</summary>
public sealed partial class RegistryEndpointsUnitTests
{
    [Fact]
    public async Task PostChannelCheck_HostWithNoWriter_GivesTheCheckOfTheCommandsOfTenantKeyNone()
    {
        // Arrange
        var keys = new List<TenantKey>();
        var commands = Substitute.For<IRegistryCommands>();
        commands.CheckChannelAsync(new ChannelId("Sms"), Arg.Any<CancellationToken>()).Returns(
            ValueTask.FromResult(ErrorOr<ChannelCheck>.ForValue(new ChannelCheck(new ChannelId("Sms"), 503, "Unavailable", Changed: true))));
        await using var host = await RegistryHost.StartAsync(withWriter: false, configure: builder => builder.UseRegistryCommands((_, key) =>
        {
            keys.Add(key);
            return commands;
        }));

        // Act
        var response = await host.Client.PostAsync(new Uri("/registry/channels/Sms/check", UriKind.Relative), content: null, Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK, """{"channel":"Sms","errorCode":503,"errorName":"Unavailable","changed":true}""", 1),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), keys.Count(static key => key == TenantKey.None)));
    }

    [Theory]
    [InlineData("not-a-name", HttpStatusCode.BadRequest)]
    [InlineData("Unknown", HttpStatusCode.NotFound)]
    public async Task PostChannelCheck_ChannelThatIsNotValidOrNotRegistered_GivesItsStatus(string channel, HttpStatusCode expected)
    {
        // Act
        var response = await _host.Client.PostAsync(new Uri($"/registry/channels/{channel}/check", UriKind.Relative), content: null, Ct);

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }
}
