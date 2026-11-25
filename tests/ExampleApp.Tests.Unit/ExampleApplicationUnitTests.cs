using ExampleApp.Channels;
using ExampleApp.Startup;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ExampleApp.Tests.Unit.TestTypes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Channels.Email.DependencyInjection;
using ExampleApp.Notices;
using MsgHatch.Channels.Broker;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit;

/// <summary>
/// The whole web app on a <c>TestServer</c>: its startup work (D157), its dispatch loops with no poll (D102), its
/// startup validation, and the sign-in with a token in the form of <c>dotnet user-jwts</c> (D158).
/// </summary>
[Collection(SequentialTelemetry.Name)]
public sealed class ExampleApplicationUnitTests : IDisposable
{
    private const string _issuer = "dotnet-user-jwts";
    private const string _audience = "http://localhost:5080";

    /// <summary>The event id of a claim of the outbox dispatch store: the record has the count and the down reason of the claim.</summary>
    private const int _outboxClaimId = 1304;
    private static readonly byte[] _signingKey = [.. Enumerable.Range(1, 32).Select(static value => (byte)value)];
    private readonly Dictionary<string, string?> _configuration = TestDatabases.NewSection();

    public ExampleApplicationUnitTests()
    {
        _configuration["Authentication:Schemes:Bearer:ValidIssuer"] = _issuer;
        _configuration["Authentication:Schemes:Bearer:ValidAudiences:0"] = _audience;
        _configuration["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = _issuer;
        _configuration["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(_signingKey);
    }

    private string? _templateCopy;

    public void Dispose()
    {
        _configuration.Delete();
        if (_templateCopy is not null)
        {
            Directory.Delete(_templateCopy, recursive: true);
        }
    }

    [Fact]
    public async Task Build_FirstStart_SendsTheEmailsOfTheSubscriptionExampleAndOfThePasswordReset()
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await ExampleStartup.RunAsync(example.App.Services, Ct);

        // Act
        await example.App.StartAsync(Ct);

        // Assert
        var emails = await example.App.Services.WaitForLogsAsync(ExampleLoggerExtensions.EmailSentId, count: 7);
        Assert.Equal(
            [
                "admin@acme.example | B értesítés: A new invoice is open.",
                "client1@acme.example | Notification A: Your report is ready.",
                "client1@acme.example | Notification B: A new invoice is open.",
                "client1@acme.example | Reset your password",
                "client2@acme.example | A értesítés: Your report is ready.",
                "client2@acme.example | D értesítés: A contract changed.",
                "client2@acme.example | Notification E: The weekly summary is ready.",
            ],
            emails.Select(static email => $"{email.Field("To")} | {email.Field("Subject")}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Build_BrokerSubscriptionForD_GivesEachBrokerMessageToTheWolverineHandlerOfTheLocalQueue()
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.Services.SeedScenarioAsync(TenantKey.None);
        var broker = new SubscriptionDraft(
            SubscriptionOwner.Default, new MessageType("D"), SubscriptionKind.Channel, BrokerChannel.Id, new BrokerChannelSettings());
        await using (var scope = example.App.Services.CreateAsyncScope())
        {
            Assert.False((await ExampleProcess.CommandsOf(scope.ServiceProvider, TenantKey.None).CreateAsync(broker, Ct)).IsError);
        }

        await ExampleStartup.RunAsync(example.App.Services, Ct);

        // Act
        await example.App.StartAsync(Ct);

        // Assert
        var messages = await example.App.Services.WaitForLogsAsync(ExampleLoggerExtensions.BrokerMessageReceivedId, count: 2);
        Assert.Equal(
            ["D: A contract changed.", "D: A contract changed."],
            messages.Select(static message => $"{message.Field("Type")}: {message.Field("Text")}"));
    }

    [Fact]
    public async Task MeRoute_TokenOfUser1_GivesTheSubscriptionsOfUser1()
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await ExampleStartup.RunAsync(example.App.Services, Ct);
        await example.App.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ExampleApplication.SubscriptionsPrefix}/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf("1"));

        // Act
        using var response = await example.App.GetTestClient().SendAsync(request, Ct);

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(
            (HttpStatusCode.OK, "B E"),
            (response.StatusCode, string.Join(' ', body.RootElement.EnumerateArray().Select(static s => s.GetProperty("messageType").GetString()).Order())));
    }

    [Theory]
    [InlineData("/channels", ExampleApplication.DeliveryAdmin, HttpStatusCode.OK)]
    [InlineData("/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", ExampleApplication.DeliveryContent, HttpStatusCode.NotFound)]
    [InlineData("/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", ExampleApplication.DeliveryAdmin, HttpStatusCode.Forbidden)]
    public async Task DeliveryRoute_RoleOfTheToken_PassesOnlyThePolicyOfItsGroup(string path, string role, HttpStatusCode expected)
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ExampleApplication.DeliveryPrefix}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf("1", role));

        // Act
        using var response = await example.App.GetTestClient().SendAsync(request, Ct);

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task PauseRoute_TwoHostsOnOneFile_StopsTheEmailSendsOfThePublisherHostUntilAResume()
    {
        // Arrange
        _configuration["MsgHatch:Roles"] = nameof(MsgHatchRoles.Consumer);
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);
        var time = new WaitRecordingTimeProvider();
        await using var publisher = BuildPublisher(time);
        await using var worker = await RunningWorker.StartAsync(publisher, Ct);
        var poll = await time.NextWaitAsync();
        await example.App.Services.SeedScenarioAsync(TenantKey.None);
        using var client = example.App.GetTestClient();
        var token = TokenOf("1", ExampleApplication.DeliveryAdmin);
        var paused = await PostAsync(client, "/channels/Email/pause", token);
        await example.App.Services.ProcessAsync(new EventA([1], null, "a"));
        time.Advance(poll);
        var claimWhilePaused = (await publisher.WaitForLogsAsync(_outboxClaimId, count: 2))[^1];
        var waitWhilePaused = await time.NextWaitAsync();
        var resumed = await PostAsync(client, "/channels/Email/resume", token);

        // Act
        time.Advance(waitWhilePaused);

        // Assert
        var emails = await publisher.WaitForLogsAsync(ExampleLoggerExtensions.EmailSentId, count: 1);
        Assert.Equal(
            (HttpStatusCode.NoContent, HttpStatusCode.NoContent, "0", "Operator"),
            (paused, resumed, claimWhilePaused.Field("Count"), claimWhilePaused.Field("DownReason")));
        Assert.Equal(["client1@acme.example | Notification A: a"], emails.Select(static email => $"{email.Field("To")} | {email.Field("Subject")}"));
    }

    [Theory]
    [InlineData(ExampleApplication.RegistryAdmin, HttpStatusCode.OK)]
    [InlineData(ExampleApplication.DeliveryAdmin, HttpStatusCode.Forbidden)]
    public async Task RegistryRoute_RoleOfTheToken_PassesOnlyTheRegistryPolicy(string role, HttpStatusCode expected)
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ExampleApplication.RegistryPrefix}/channels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf("1", role));

        // Act
        using var response = await example.App.GetTestClient().SendAsync(request, Ct);

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task RegistryRoute_DisabledMessageTypeA_TheConsumerStoresNoMessageOfA()
    {
        // Arrange
        _configuration["MsgHatch:Roles"] = nameof(MsgHatchRoles.Consumer);
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);
        await example.App.Services.SeedScenarioAsync(TenantKey.None);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ExampleApplication.RegistryPrefix}/message-types/A")
        {
            Content = new StringContent("""{"enabled":false,"revision":0}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf("1", ExampleApplication.RegistryAdmin));
        using var saved = await example.App.GetTestClient().SendAsync(request, Ct);

        // Act
        var result = await example.App.Services.ProcessAsync(new EventA([1], null, "a"));

        // Assert
        Assert.Equal((HttpStatusCode.OK, 0, 0), (saved.StatusCode, result.Value.Stored, result.Value.Failed));
    }

    [Fact]
    public async Task MeRoute_NoToken_Gives401()
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);

        // Act
        using var response = await example.App.GetTestClient().GetAsync($"{ExampleApplication.SubscriptionsPrefix}/me", Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ExampleApplication.TemplateAdmin, HttpStatusCode.OK)]
    [InlineData(ExampleApplication.RegistryAdmin, HttpStatusCode.Forbidden)]
    public async Task TemplateRoute_RoleOfTheToken_PassesOnlyTheTemplatePolicy(string role, HttpStatusCode expected)
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);

        // Act
        using var response = await SendAsync(example, HttpMethod.Get, ExampleApplication.TemplatesPrefix, body: null, role);

        // Assert
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task TemplateRoute_PutOfASubject_TheNextGetGivesTheNewTextAndTheBuildOutputKeepsItsText()
    {
        // Arrange
        _configuration["MsgHatch:FileTemplates:RootPath"] = CopyOfTheTemplates();
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);
        const string path = $"{ExampleApplication.TemplatesPrefix}/A/en/subject/PlainText";
        using var read = await SendAsync(example, HttpMethod.Get, path, body: null, ExampleApplication.TemplateAdmin);
        var version = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("version").GetInt64();

        // Act
        using var saved = await SendAsync(
            example, HttpMethod.Put, path, "{\"text\":\"Changed A: {{Text}}\",\"version\":" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", ExampleApplication.TemplateAdmin);
        using var changed = await SendAsync(example, HttpMethod.Get, path, body: null, ExampleApplication.TemplateAdmin);

        // Assert
        var text = JsonDocument.Parse(await changed.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("text").GetString();
        Assert.Equal(
            (HttpStatusCode.OK, "Changed A: {{Text}}", "Notification A: {{Text}}"),
            (saved.StatusCode, text, File.ReadAllText(Path.Combine(ExampleSetup.TemplateFolder, "A.en.subject.txt"))));
    }

    [Theory]
    [InlineData("A", "placeholder")]
    [InlineData("F", "liquid")]
    public async Task PartsRoute_EmailOfAMessageType_NamesTheEngineOfEachPart_D253(string messageType, string engine)
    {
        // Arrange
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);

        // Act
        using var response = await SendAsync(
            example, HttpMethod.Get, $"{ExampleApplication.TemplatesPrefix}/parts?messageType={messageType}", body: null, ExampleApplication.TemplateAdmin);

        // Assert
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        List<JsonElement> parts = [.. body.RootElement.EnumerateArray()];
        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Equal([engine], part.GetProperty("engines").EnumerateArray().Select(static name => name.GetString())));
    }

    [Fact]
    public async Task TemplateRoute_PutOfAnUnknownPlaceholder_Gives422()
    {
        // Arrange
        _configuration["MsgHatch:FileTemplates:RootPath"] = CopyOfTheTemplates();
        await using var example = ExampleWebApp.Build(_configuration);
        await example.App.StartAsync(Ct);

        // Act
        using var response = await SendAsync(
            example, HttpMethod.Put, $"{ExampleApplication.TemplatesPrefix}/A/de/subject/PlainText", """{"text":"A: {{Txet}}","version":0}""",
            ExampleApplication.TemplateAdmin);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(ExampleWebApp example, HttpMethod method, string path, string? body, string role)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenOf("1", role));
        return await example.App.GetTestClient().SendAsync(request, Ct);
    }

    /// <summary>Copies the templates of the build output into a temporary folder, so a write of a test never changes the build output.</summary>
    private string CopyOfTheTemplates()
    {
        _templateCopy = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_templateCopy);
        foreach (var file in Directory.EnumerateFiles(ExampleSetup.TemplateFolder))
        {
            File.Copy(file, Path.Combine(_templateCopy, Path.GetFileName(file)));
        }

        return _templateCopy;
    }

    /// <summary>A token in the form of <c>dotnet user-jwts create --name {user} --role {role}</c> (D158, D165).</summary>
    private static string TokenOf(string user, params string[] roles) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            Claims = new Dictionary<string, object> { ["sub"] = user, ["unique_name"] = user, ["role"] = roles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256),
        });

    private ServiceProvider BuildPublisher(WaitRecordingTimeProvider time)
    {
        var services = CreateServices(_configuration, time);
        services.AddExampleMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseEmailClient<LogEmailClient>();
        var publisher = services.Build();
        publisher.GetRequiredService<IStartupValidator>().Validate();
        return publisher;
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ExampleApplication.DeliveryPrefix}{path}")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }
}
