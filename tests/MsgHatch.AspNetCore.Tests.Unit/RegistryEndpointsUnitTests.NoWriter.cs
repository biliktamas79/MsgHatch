using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The registry endpoints with the in-memory reader of a setup in code, so with no writer (section 4.20).</summary>
public sealed partial class RegistryEndpointsUnitTests
{
    [Fact]
    public async Task GetChannels_NoWriter_GivesEachChannelWithTheRevision0()
    {
        // Arrange
        await using var host = await RegistryHost.StartAsync(withWriter: false);

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            """[{"id":"Test","name":"Test channel","description":null,"features":"None","enabled":true,"revision":0,"current":null,"entry":null},"""
            + """{"id":"Second","name":"Second test channel","description":null,"features":"GroupDelivery","enabled":true,"revision":0,"current":null,"entry":null}]""",
            json);
    }

    [Fact]
    public async Task GetMessageTypes_NoWriter_GivesEachMessageTypeWithTheRevision0()
    {
        // Arrange
        await using var host = await RegistryHost.StartAsync(withWriter: false);

        // Act
        var json = await host.Client.GetStringAsync(new Uri("/registry/message-types", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            """[{"name":"TestEvent","displayName":null,"description":null,"channels":["Test","Second"],"enabled":true,"disabledChannels":[],"revision":0,"entry":null}]""",
            json);
    }

    public static TheoryData<string, string, string?> NoWriterChangeRows => new()
    {
        { "PUT", "/registry/channels/Test", """{"enabled":true,"revision":0}""" },
        { "DELETE", "/registry/channels/Test", null },
        { "PUT", "/registry/message-types/TestEvent", """{"enabled":true,"revision":0}""" },
        { "DELETE", "/registry/message-types/TestEvent", null },
    };

    [Theory]
    [MemberData(nameof(NoWriterChangeRows))]
    public async Task Change_NoWriter_Gives404WithTheErrorTypeAndTheMessageOfTheCommand(string method, string path, string? body)
    {
        // Arrange
        await using var host = await RegistryHost.StartAsync(withWriter: false);
        await using var scope = host.Services.CreateAsyncScope();
        var command = await scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(TenantKey.None).ResetChannelAsync(TestChannel.Id, Ct);

        // Act
        var problem = await ProblemOf(await host.Client.SendAsync(RequestOf(method, path, body), Ct));

        // Assert
        Assert.Equal(
            ($"{HttpStatusCode.NotFound}", (string?)Errors.NotFound.Name, command?.Message, (int?)Errors.NotFound.Code),
            (problem.Status, problem.Title, problem.Detail, problem.ErrorCode));
    }
}
