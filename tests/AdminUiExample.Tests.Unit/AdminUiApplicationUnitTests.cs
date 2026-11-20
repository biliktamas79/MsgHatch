using System.Text.RegularExpressions;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Verification;
using MsgHatch.Templates;
using MsgHatch.Templates.Management;

namespace AdminUiExample.Tests.Unit;

public sealed class AdminUiApplicationUnitTests
{
    private const string _testToken = "test-token-of-the-unit-tests";

    [Fact]
    public async Task Build_ClientHost_SendsTheCallOfEachInterfaceOverHttpWithTheTokenOfTheConfiguration()
    {
        var handler = new RecordingHandler();
        await using var app = Build(handler);
        await using var scope = app.Services.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        await Service<ISubscriptionQueries>(scope).GetChannelsAsync(ct);
        await Service<ISubscriptionCommands>(scope).DeleteAsync(id, ct);
        await Service<IRoutingVerifier>(scope).VerifyAsync(new RoutingVerificationRequest([new UserId("1")], ["en"]), ct);
        await Service<ITemplateQueries>(scope).GetTemplatesAsync(new TemplateQuery(), ct);
        await Service<ITemplateCommands>(scope).DeleteTemplateAsync(new TemplateLookup(new MessageType("Requests"), "en", null, "subject", TemplatePartKind.PlainText), ct);

        Assert.Equal(
            [
                "GET http://localhost:5080/api/subscriptions/channels",
                "DELETE http://localhost:5080/api/subscriptions/00000000-0000-0000-0000-000000000001",
                "POST http://localhost:5080/api/subscriptions/verification",
                "GET http://localhost:5080/api/templates",
                "DELETE http://localhost:5080/api/templates/Requests/en/subject/PlainText",
            ],
            handler.Requests.Select(static request => request.Call));
        Assert.All(handler.Requests, static request => Assert.Equal("Bearer " + _testToken, request.Authorization));
    }

    [Fact]
    public async Task Build_ClientHost_SendsTheRegistryQueriesOverHttpWithTheTokenOfTheConfiguration()
    {
        var handler = new RecordingHandler();
        await using var app = Build(handler);
        await using var scope = app.Services.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;

        await Service<IRegistryQueries>(scope).GetChannelsAsync(ct);
        await Service<IRegistryQueries>(scope).GetMessageTypesAsync(ct);

        Assert.Equal(
            [
                ("GET http://localhost:5080/api/registry/channels", "Bearer " + _testToken),
                ("GET http://localhost:5080/api/registry/message-types", "Bearer " + _testToken),
            ],
            handler.Requests);
    }

    [Fact]
    public async Task Build_ClientHost_SendsTheDeliveryCallsOverHttpWithTheTokenOfTheConfiguration()
    {
        var handler = new RecordingHandler();
        await using var app = Build(handler);
        await using var scope = app.Services.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;

        await Service<IDeliveryQueries>(scope).GetChannelsAsync(ct);
        await Service<IDeliveryCommands>(scope).DeleteTestDataAsync(ct);

        Assert.Equal(
            [
                ("GET http://localhost:5080/api/delivery/channels", "Bearer " + _testToken),
                ("DELETE http://localhost:5080/api/delivery/test-data", "Bearer " + _testToken),
            ],
            handler.Requests);
    }

    [Fact]
    public async Task Get_TemplateListWithNoApi_ShowsTheTextOfUnavailable()
    {
        await using var app = Build(new RecordingHandler(available: false));
        await app.StartAsync(TestContext.Current.CancellationToken);

        var page = await app.GetTestClient().GetStringAsync(new Uri("/msghatch/templates", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains(AdminErrors.UnavailableText, page, StringComparison.Ordinal);
        Assert.Contains("bootstrap@5.3.3", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_Page_LoadsTheThemeScriptBeforeTheStylesAndHasTheThemeSwitch()
    {
        await using var app = Build(new RecordingHandler(available: false));
        await app.StartAsync(TestContext.Current.CancellationToken);
        var page = await app.GetTestClient().GetStringAsync(new Uri("/msghatch/templates", UriKind.Relative), TestContext.Current.CancellationToken);
        var script = Regex.Match(page, "<script src=\"(?<src>theme[^\"]*\\.js)\"").Groups["src"].Value;

        Assert.Equal(
            (true, true, true),
            (script.Length > 0, page.IndexOf(script, StringComparison.Ordinal) < page.IndexOf("bootstrap@5.3.3", StringComparison.Ordinal),
                page.Contains("data-theme-toggle", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Get_Home_HasTheMenuLinksInTheOrderOfTheAdminPages()
    {
        await using var app = Build(new RecordingHandler(available: false));
        await app.StartAsync(TestContext.Current.CancellationToken);
        var page = await app.GetTestClient().GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        var links = Regex.Matches(page, "<a\\b(?=[^>]*\\bclass=\"nav-link[^\"]*\")[^>]*>(?<text>[^<]*)</a>");

        Assert.Equal(
            ["Channels", "Message types", "Subscriptions", "Templates", "Verification", "Traffic", "Outbox", "Inbox"],
            links.Select(static match => match.Groups["text"].Value));
    }

    [Theory]
    [InlineData("/msghatch/subscriptions", "Subscriptions")]
    [InlineData("/msghatch/subscriptions/00000000-0000-0000-0000-000000000001", "Subscriptions")]
    [InlineData("/msghatch/subscriptions/verification", "Verification")]
    [InlineData("/msghatch/templates", "Templates")]
    [InlineData("/msghatch/channels", "Channels")]
    [InlineData("/msghatch/channels/Email", "Channels")]
    [InlineData("/msghatch/channels/Email/edit", "Channels")]
    [InlineData("/msghatch/message-types", "Message types")]
    [InlineData("/msghatch/message-types/A/edit", "Message types")]
    [InlineData("/msghatch/traffic", "Traffic")]
    [InlineData("/msghatch/outbox", "Outbox")]
    [InlineData("/msghatch/outbox/00000000-0000-0000-0000-000000000001", "Outbox")]
    [InlineData("/msghatch/inbox", "Inbox")]
    [InlineData("/msghatch/inbox/00000000-0000-0000-0000-000000000001", "Inbox")]
    public async Task Get_Page_MarksOnlyTheMenuLinkOfItsPageAsActive(string path, string link)
    {
        await using var app = Build(new RecordingHandler(available: false));
        await app.StartAsync(TestContext.Current.CancellationToken);
        var page = await app.GetTestClient().GetStringAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        var active = Regex.Matches(page, "<a\\b(?=[^>]*\\bclass=\"nav-link active\")[^>]*>(?<text>[^<]*)</a>");

        Assert.Equal([link], active.Select(static match => match.Groups["text"].Value));
    }

    [Fact]
    public async Task Start_NoApiToken_StopsTheStartWithAnOptionsError()
    {
        await using var app = Build(new RecordingHandler(), token: null);

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    private static WebApplication Build(RecordingHandler handler, string? token = _testToken)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(AdminUiApplication).Assembly.GetName().Name });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [$"{AdminUiExampleOptions.SectionName}:ApiToken"] = token });
        builder.Services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = handler));
        return AdminUiApplication.Build(builder);
    }

    private static TService Service<TService>(AsyncServiceScope scope)
        where TService : notnull =>
        scope.ServiceProvider.GetRequiredKeyedService<TService>(TenantKey.None);

    private sealed class RecordingHandler(bool available = true) : HttpMessageHandler
    {
        public List<(string Call, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(($"{request.Method} {request.RequestUri}", request.Headers.Authorization?.ToString()));
            return available
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") })
                : throw new HttpRequestException("No server listens on the port.");
        }
    }
}
