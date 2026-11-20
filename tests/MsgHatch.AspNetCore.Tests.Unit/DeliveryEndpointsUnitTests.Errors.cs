using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MsgHatch.Api.Contracts;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

public sealed partial class DeliveryEndpointsUnitTests
{
    [Fact]
    public async Task GetChannels_TenantId_UsesTheQueriesOfThatTenant()
    {
        // Act
        await _host.Client.GetAsync(new Uri("/delivery/channels?tenantId=t2", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((1, 0), (_host.T2Queries.ReceivedCalls().Count(), _host.Calls.Count()));
    }

    [Fact]
    public async Task GetChannels_NoTenantId_UsesTheQueriesOfNoTenant()
    {
        // Act
        await _host.Client.GetAsync(new Uri("/delivery/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((1, 0), (_host.Queries.ReceivedCalls().Count(), _host.T2Calls.Count()));
    }

    [Fact]
    public async Task RetryOutgoing_TenantId_UsesTheCommandsOfThatTenant()
    {
        // Act
        await _host.Client.SendAsync(RequestOf("POST", "/delivery/outbox/retry?tenantId=t2", """{"channel":"Test"}"""), Ct);

        // Assert
        Assert.Equal((1, 0), (_host.T2Commands.ReceivedCalls().Count(), _host.Calls.Count()));
    }

    [Theory]
    [InlineData("POST", "/delivery/channels/Test/pause", "{}")]
    [InlineData("POST", "/delivery/channels/Test/resume", null)]
    [InlineData("POST", "/delivery/purge", """{"finishedBefore":"2030-01-01T00:00:00Z","maxCount":10}""")]
    [InlineData("DELETE", "/delivery/test-data", null)]
    public async Task GlobalRoute_TenantId_Gives400WithTheErrorTypeAndCallsNoService(string method, string path, string? body)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf(method, $"{path}?tenantId=t2", body), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.BadRequest, (int?)Errors.ValidationFailed.Code, 0, 0),
            (response.StatusCode, await ErrorCodeOf(response), _host.Calls.Count(), _host.T2Calls.Count()));
    }

    public static TheoryData<int, HttpStatusCode> ErrorRows => new()
    {
        { 400, HttpStatusCode.BadRequest },
        { 403, HttpStatusCode.Forbidden },
        { 404, HttpStatusCode.NotFound },
        { 409, HttpStatusCode.Conflict },
        { 410, HttpStatusCode.Gone },
        { 415, HttpStatusCode.InternalServerError },
        { 422, HttpStatusCode.UnprocessableEntity },
        { 429, HttpStatusCode.TooManyRequests },
        { 500, HttpStatusCode.InternalServerError },
        { 503, HttpStatusCode.ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task Routes_QueryOrCommandError_GiveTheStatusOfErrorProblemsWithTheDetailAndTheErrorType(int errorCode, HttpStatusCode expected)
    {
        // Arrange
        var error = new Error(Errors.Find(errorCode)!, "The store failed.");
        _host.Queries.GetOutgoingMessageAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageDetail>.ForError(error)));
        _host.Queries.GetOutgoingContentAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessageContent>.ForError(error)));
        _host.Commands.ResumeChannelAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        _host.Commands.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<DeletedMessages>.ForError(error)));
        (string, string?, string?, int?) problem = ($"{expected}", error.Type.Name, error.Message, error.Type.Code);

        // Act
        var detail = await ProblemOf(await _host.Client.SendAsync(RequestOf("GET", $"/delivery/outbox/{_id}", body: null), Ct));
        var content = await ProblemOf(await _host.Client.SendAsync(RequestOf("GET", $"/delivery/outbox/{_id}/content", body: null), Ct));
        var resume = await ProblemOf(await _host.Client.SendAsync(RequestOf("POST", "/delivery/channels/Test/resume", body: null), Ct));
        var testData = await ProblemOf(await _host.Client.SendAsync(RequestOf("DELETE", "/delivery/test-data", body: null), Ct));

        // Assert
        Assert.Equal((problem, problem, problem, problem), (detail, content, resume, testData));
    }

    [Fact]
    public async Task DeleteTestData_Commands_CallsTheCommandsOfNoTenantAndGivesBothCounts()
    {
        // Arrange
        _host.Commands.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<DeletedMessages>.ForValue(new DeletedMessages(2, 5))));

        // Act
        var response = await _host.Client.SendAsync(RequestOf("DELETE", "/delivery/test-data", body: null), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK, """{"inboxMessages":2,"outboxMessages":5}""", 1),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), _host.Commands.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData("/delivery/outbox?status=Unknown")]
    [InlineData("/delivery/outbox?limit=ten")]
    [InlineData("/delivery/outbox?after=2030-01-01_x")]
    [InlineData("/delivery/outbox?targetKind=User")]
    [InlineData("/delivery/outbox?ids=not-a-guid")]
    [InlineData("/delivery/inbox?createdFrom=yesterday")]
    [InlineData("/delivery/traffic?includeTest=yes")]
    [InlineData("/delivery/channels?tenantId=")]
    [InlineData("/delivery/outbox?channel=bad-name")]
    [InlineData("/delivery/inbox?messageType=1x")]
    public async Task Query_InvalidValue_Gives400WithTheErrorTypeAndCallsNoService(string path)
    {
        // Act
        var response = await _host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.BadRequest, (int?)Errors.ValidationFailed.Code, 0),
            (response.StatusCode, await ErrorCodeOf(response), _host.Calls.Count()));
    }

    [Fact]
    public async Task Pause_BodyThatIsNotJson_Gives415AndCallsNoService()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/delivery/channels/Test/pause", UriKind.Relative))
        {
            Content = new StringContent("""{"note":"x"}""", Encoding.UTF8, "text/plain"),
        };

        // Act
        var response = await _host.Client.SendAsync(request, Ct);

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, _host.Calls.Count()));
    }

    [Fact]
    public async Task Purge_BodyWithNoMaxCount_Gives400WithTheErrorTypeAndCallsNoService()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", "/delivery/purge", """{"finishedBefore":"2030-01-01T00:00:00Z"}"""), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.BadRequest, (int?)Errors.ValidationFailed.Code, 0),
            (response.StatusCode, await ErrorCodeOf(response), _host.Calls.Count()));
    }

    [Fact]
    public async Task RetryOutgoing_BodyThatIsNotJson_Gives400WithTheErrorTypeAndCallsNoService()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", "/delivery/outbox/retry", "{ not json"), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.BadRequest, (int?)Errors.ValidationFailed.Code, 0),
            (response.StatusCode, await ErrorCodeOf(response), _host.Calls.Count()));
    }

    [Fact]
    public async Task ContentRoute_HostWithOnlyTheOverviewGroup_Gives404()
    {
        // Arrange
        await using var host = await DeliveryHost.StartAsync(content: false);

        // Act
        var response = await host.Client.GetAsync(new Uri($"/delivery/outbox/{_id}/content", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.NotFound, 0), (response.StatusCode, host.Calls.Count()));
    }

    [Fact]
    public async Task OverviewRoute_HostWithOnlyTheContentGroup_Gives404()
    {
        // Arrange
        await using var host = await DeliveryHost.StartAsync(overview: false);

        // Act
        var response = await host.Client.GetAsync(new Uri("/delivery/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.NotFound, 0), (response.StatusCode, host.Calls.Count()));
    }

    [Theory]
    [InlineData(DeliveryHost.AdminRole, "/delivery/channels", HttpStatusCode.OK)]
    [InlineData(DeliveryHost.ContentRole, "/delivery/channels", HttpStatusCode.Forbidden)]
    [InlineData(DeliveryHost.ContentRole, "/delivery/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", HttpStatusCode.OK)]
    [InlineData(DeliveryHost.AdminRole, "/delivery/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", HttpStatusCode.Forbidden)]
    public async Task Group_PolicyOfTheOtherGroup_DoesNotApply(string role, string path, HttpStatusCode expected)
    {
        // Arrange
        _host.Queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelStatus>>.ForValue([])));
        _host.Queries.GetOutgoingContentAsync(default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessageContent>.ForValue(new MessageContent(Guid.Empty, "{}"))));
        using var client = _host.ClientWithRoles(role);

        // Act
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }
}
