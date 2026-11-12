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
using MsgHatch.Broker;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
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
    private static readonly byte[] _signingKey = [.. Enumerable.Range(1, 32).Select(static value => (byte)value)];
    private readonly Dictionary<string, string?> _configuration = TestDatabases.NewSection();

    public ExampleApplicationUnitTests()
    {
        _configuration["Authentication:Schemes:Bearer:ValidIssuer"] = _issuer;
        _configuration["Authentication:Schemes:Bearer:ValidAudiences:0"] = _audience;
        _configuration["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = _issuer;
        _configuration["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(_signingKey);
    }

    public void Dispose() => _configuration.Delete();

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
            Assert.False((await ExampleProcess.ManagerOf(scope.ServiceProvider, TenantKey.None).CreateAsync(broker, Ct)).IsError);
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

    private static string TokenOf(string user) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            Claims = new Dictionary<string, object> { ["sub"] = user, ["unique_name"] = user },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256),
        });
}
