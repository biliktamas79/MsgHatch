using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>
/// The requests and the failures of the HTTP registry queries on a stub handler (D233, D269, D271): no <c>tenantId</c>, the
/// error of a network failure and of a rejected token, and a response that the contract does not allow.
/// </summary>
public sealed class RegistryApiClientRequestsUnitTests
{
    private const string _channel = """{"id":"Email","name":"Email","description":null,"features":"Languages, RichText","enabled":true,"revision":0}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<HttpStatusCode> RejectedRows => [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden];

    public static TheoryData<string, string> NotAllowedRows => new()
    {
        { "a null item", "[null]" },
        { "a channel id that is not a name", $"[{_channel.Replace("\"Email\",\"name\"", "\"not a name\",\"name\"", StringComparison.Ordinal)}]" },
        { "a null name", $"[{_channel.Replace("\"name\":\"Email\"", "\"name\":null", StringComparison.Ordinal)}]" },
        { "the JSON null", "null" },
    };

    [Fact]
    public async Task Queries_KeyOfATenant_SendNoTenantId()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        await using var provider = AProvider(handler);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.For(new TenantId("t1")));

        // Act
        await queries.GetChannelsAsync(Ct);
        await queries.GetMessageTypesAsync(Ct);
        await queries.GetChannelHistoryAsync(new ChannelId("Email"), Ct);

        // Assert
        Assert.Equal(
            ["/registry/channels", "/registry/message-types", "/registry/channels/Email/history"], handler.Uris.Select(static uri => uri.PathAndQuery));
    }

    [Fact]
    public async Task Queries_NetworkError_GiveUnavailableWithTheException()
    {
        // Arrange
        var exception = new HttpRequestException(HttpRequestError.ConnectionError);
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, "[]", exception));
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IRegistryQueries>();

        // Act
        var channels = await queries.GetChannelsAsync(Ct);
        var types = await queries.GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal((Errors.Unavailable, exception, Errors.Unavailable), (channels.Error?.Type, channels.Error?.Exception, types.Error?.Type));
    }

    [Theory]
    [MemberData(nameof(RejectedRows))]
    public async Task Queries_TokenThatTheHostRejects_GiveRejected(HttpStatusCode status)
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(status, $$"""{"title":"{{status}}","status":{{(int)status}}}"""));
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IRegistryQueries>();

        // Act
        var channels = await queries.GetChannelsAsync(Ct);
        var types = await queries.GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal((Errors.Rejected, Errors.Rejected), (channels.Error?.Type, types.Error?.Type));
    }

    [Theory]
    [MemberData(nameof(NotAllowedRows))]
    public async Task GetChannelsAsync_ResponseThatTheContractDoesNotAllow_GivesSerializationFailed(string row, string json)
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var channels = await scope.ServiceProvider.GetRequiredService<IRegistryQueries>().GetChannelsAsync(Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, channels.Error?.Type));
    }

    [Fact]
    public async Task GetMessageTypesAsync_ChannelThatIsNotAName_GivesSerializationFailed()
    {
        // Arrange
        const string json = """[{"name":"Invoices","displayName":null,"description":null,"channels":["not a name"],"enabled":true,"disabledChannels":[],"revision":0}]""";
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var types = await scope.ServiceProvider.GetRequiredService<IRegistryQueries>().GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, types.Error?.Type);
    }

    [Fact]
    public async Task GetChannelsAsync_BodyOfAServerWithNoChannelState_GivesAChannelWithNoCurrentRow()
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, $"[{_channel}]"));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var channels = await scope.ServiceProvider.GetRequiredService<IRegistryQueries>().GetChannelsAsync(Ct);

        // Assert
        Assert.Null(channels.Value[0].Current);
    }

    [Fact]
    public async Task GetChannelHistoryAsync_RowWithAChannelThatIsNotAName_GivesSerializationFailed()
    {
        // Arrange
        const string json = """
            [{"channel":"not a name","version":1,"kind":"Down","sinceUtc":"2030-01-01T00:00:00Z","previousKind":null,"previousDurationMs":null,"reason":"Operator",
            "nextCheckAtUtc":null,"errorCode":null,"errorName":null,"note":null}]
            """;
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var history = await scope.ServiceProvider.GetRequiredService<IRegistryQueries>().GetChannelHistoryAsync(new ChannelId("Email"), Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, history.Error?.Type);
    }

    [Fact]
    public async Task CheckChannelAsync_ErrorCodeWithNoErrorName_GivesSerializationFailed()
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, """{"channel":"Email","errorCode":503,"errorName":null,"changed":true}"""));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var check = await scope.ServiceProvider.GetRequiredService<IRegistryCommands>().CheckChannelAsync(new ChannelId("Email"), Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, check.Error?.Type);
    }

    private static ServiceProvider AProvider(StubHandler handler) =>
        TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseRegistryApiClient(HttpRegistryHost.BaseAddress, http => http.ConfigurePrimaryHttpMessageHandler(() => handler)).Services.Build();
}
