using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

public sealed partial class DeliveryEndpointsUnitTests
{
    [Fact]
    public async Task GetChannels_TenantId_UsesTheManagerOfThatTenant()
    {
        // Act
        await _host.Client.GetAsync(new Uri("/delivery/channels?tenantId=t2", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((1, 0), (_host.T2Manager.ReceivedCalls().Count(), _host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task GetChannels_NoTenantId_UsesTheManagerOfNoTenant()
    {
        // Act
        await _host.Client.GetAsync(new Uri("/delivery/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((1, 0), (_host.Manager.ReceivedCalls().Count(), _host.T2Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData("/delivery/channels/Test/pause", "{}")]
    [InlineData("/delivery/channels/Test/resume", null)]
    [InlineData("/delivery/purge", """{"finishedBefore":"2030-01-01T00:00:00Z","maxCount":10}""")]
    public async Task GlobalRoute_TenantId_Gives400AndCallsNoManager(string path, string? body)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", $"{path}?tenantId=t2", body), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.BadRequest, 0, 0),
            (response.StatusCode, _host.Manager.ReceivedCalls().Count(), _host.T2Manager.ReceivedCalls().Count()));
    }

    public static TheoryData<string, HttpStatusCode> ErrorRows => new()
    {
        { nameof(Errors.ValidationFailed), HttpStatusCode.BadRequest },
        { nameof(Errors.NotFound), HttpStatusCode.NotFound },
        { nameof(Errors.Unavailable), HttpStatusCode.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task ManagerError_Type_GivesItsStatusCodeWithAProblemDetailsOfTheErrorName(string errorName, HttpStatusCode expected)
    {
        // Arrange
        var type = errorName switch
        {
            nameof(Errors.ValidationFailed) => Errors.ValidationFailed,
            nameof(Errors.NotFound) => Errors.NotFound,
            _ => Errors.Unavailable,
        };
        _host.Manager.GetOutgoingMessageAsync(default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageDetail>.ForError(new Error(type, "text"))));

        // Act
        var response = await _host.Client.GetAsync(new Uri($"/delivery/outbox/{_id}", UriKind.Relative), Ct);

        // Assert
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal((expected, errorName), (response.StatusCode, problem?.Title));
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
    public async Task Query_InvalidValue_Gives400AndCallsNoManager(string path)
    {
        // Act
        var response = await _host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.BadRequest, 0), (response.StatusCode, _host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Pause_BodyThatIsNotJson_Gives415AndCallsNoManager()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/delivery/channels/Test/pause", UriKind.Relative))
        {
            Content = new StringContent("""{"note":"x"}""", Encoding.UTF8, "text/plain"),
        };

        // Act
        var response = await _host.Client.SendAsync(request, Ct);

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, _host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Purge_BodyWithNoMaxCount_Gives400AndCallsNoManager()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", "/delivery/purge", """{"finishedBefore":"2030-01-01T00:00:00Z"}"""), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.BadRequest, 0), (response.StatusCode, _host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task ContentRoute_HostWithOnlyTheOverviewGroup_Gives404()
    {
        // Arrange
        await using var host = await DeliveryHost.StartAsync(content: false);

        // Act
        var response = await host.Client.GetAsync(new Uri($"/delivery/outbox/{_id}/content", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.NotFound, 0), (response.StatusCode, host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task OverviewRoute_HostWithOnlyTheContentGroup_Gives404()
    {
        // Arrange
        await using var host = await DeliveryHost.StartAsync(overview: false);

        // Act
        var response = await host.Client.GetAsync(new Uri("/delivery/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.NotFound, 0), (response.StatusCode, host.Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData(DeliveryHost.AdminRole, "/delivery/channels", HttpStatusCode.OK)]
    [InlineData(DeliveryHost.ContentRole, "/delivery/channels", HttpStatusCode.Forbidden)]
    [InlineData(DeliveryHost.ContentRole, "/delivery/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", HttpStatusCode.OK)]
    [InlineData(DeliveryHost.AdminRole, "/delivery/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", HttpStatusCode.Forbidden)]
    public async Task Group_PolicyOfTheOtherGroup_DoesNotApply(string role, string path, HttpStatusCode expected)
    {
        // Arrange
        _host.Manager.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelStatus>>.ForValue([])));
        _host.Manager.GetOutgoingContentAsync(default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<Domain.Delivery.OutgoingMessage>.ForValue(Domain.Tests.Unit.TestTypes.TestMessages.AnOutgoingMessage())));
        using var client = _host.ClientWithRoles(role);

        // Act
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }
}
