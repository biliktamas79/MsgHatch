using System.Net;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>
/// The channel check route (D283) and the save and reset routes (D284) of the registry, which call
/// <see cref="IRegistryCommands"/> of <c>TenantKey.None</c>.
/// </summary>
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

    [Fact]
    public async Task SaveAndResetRoutes_SelectedCommands_CallTheCommandsOfTenantKeyNoneWithTheValuesOfThePathAndTheBody()
    {
        // Arrange
        var keys = new List<TenantKey>();
        var commands = Substitute.For<IRegistryCommands>();
        commands.SaveChannelAsync(default!, Ct).ReturnsForAnyArgs(static call => ValueTask.FromResult(ErrorOr<ChannelEntry>.ForValue(call.ArgAt<ChannelEntry>(0))));
        commands.SaveMessageTypeAsync(default!, Ct).ReturnsForAnyArgs(static call => ValueTask.FromResult(ErrorOr<MessageTypeEntry>.ForValue(call.ArgAt<MessageTypeEntry>(0))));
        await using var host = await RegistryHost.StartAsync(withWriter: false, configure: builder => builder.UseRegistryCommands((_, key) =>
        {
            keys.Add(key);
            return commands;
        }));

        // Act
        await host.Client.SendAsync(RequestOf("PUT", "/registry/channels/Sms", """{"displayName":"Texts","enabled":false,"revision":2}"""), Ct);
        await host.Client.SendAsync(RequestOf("PUT", "/registry/message-types/Invoices", """{"enabled":true,"revision":1,"disabledChannels":["Sms"]}"""), Ct);
        await host.Client.SendAsync(RequestOf("DELETE", "/registry/channels/Sms", body: null), Ct);
        await host.Client.SendAsync(RequestOf("DELETE", "/registry/message-types/Invoices", body: null), Ct);

        // Assert
        Assert.Equal(
            ["SaveChannelAsync Sms Texts False 2", "SaveMessageTypeAsync Invoices  True 1 Sms", "ResetChannelAsync Sms", "ResetMessageTypeAsync Invoices"],
            commands.ReceivedCalls().Select(static call => $"{call.GetMethodInfo().Name} {TextOf(call.GetArguments()[0])}"));
        Assert.Equal(4, keys.Count(static key => key == TenantKey.None));
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

    private static string? TextOf(object? argument) => argument switch
    {
        ChannelEntry entry => $"{entry.Channel.Name} {entry.DisplayName} {entry.Enabled} {entry.Revision}",
        MessageTypeEntry entry => $"{entry.Type.Name} {entry.DisplayName} {entry.Enabled} {entry.Revision} {string.Join(',', entry.DisabledChannels.Select(static channel => channel.Name))}",
        ChannelId channel => channel.Name,
        MessageType type => type.Name,
        _ => argument?.ToString(),
    };
}
