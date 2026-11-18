using System.Net;
using System.Text;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;

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
            """[{"id":"Test","name":"Test channel","description":null,"features":"None","enabled":true,"revision":0,"current":null},"""
            + """{"id":"Second","name":"Second test channel","description":null,"features":"GroupDelivery","enabled":true,"revision":0,"current":null}]""",
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
            """[{"name":"TestEvent","displayName":null,"description":null,"channels":["Test","Second"],"enabled":true,"disabledChannels":[],"revision":0}]""",
            json);
    }

    public static TheoryData<string, string> NoWriterChangeRows => new()
    {
        { "PUT", "/registry/channels/Test" },
        { "DELETE", "/registry/channels/Test" },
        { "PUT", "/registry/message-types/TestEvent" },
        { "DELETE", "/registry/message-types/TestEvent" },
    };

    [Theory]
    [MemberData(nameof(NoWriterChangeRows))]
    public async Task Change_NoWriter_HasNoRoute(string method, string path)
    {
        // Arrange
        await using var host = await RegistryHost.StartAsync(withWriter: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("""{"enabled":true,"revision":0}""", Encoding.UTF8, "application/json"),
        };

        // Act
        var response = await host.Client.SendAsync(request, Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
