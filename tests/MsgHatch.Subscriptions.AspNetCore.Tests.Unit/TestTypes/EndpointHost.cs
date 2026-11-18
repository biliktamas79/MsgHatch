using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.AspNetCore.Tests.Unit.TestTypes;

/// <summary>
/// A web application on a <see cref="TestServer"/> with the subscription endpoints, the message types A to E of the
/// scenario, and a manager substitute and a verifier substitute for <see cref="TenantKey.None"/> and for the tenant <c>t2</c>. A request with
/// the header <see cref="UserHeader"/> has a signed-in user with that id, and the header <see cref="TenantHeader"/> gives
/// the tenant of that user.
/// </summary>
public sealed class EndpointHost : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";
    public const string TenantHeader = "X-Test-Tenant";

    public static readonly TenantId T2 = new("t2");

    private readonly WebApplication _app;

    private EndpointHost(
        WebApplication app, ISubscriptionManagement manager, ISubscriptionManagement t2Manager, IRoutingVerifier verifier, IRoutingVerifier t2Verifier)
    {
        _app = app;
        Manager = manager;
        T2Manager = t2Manager;
        Verifier = verifier;
        T2Verifier = t2Verifier;
        Client = app.GetTestClient();
    }

    /// <summary>The manager of <see cref="TenantKey.None"/>.</summary>
    public ISubscriptionManagement Manager { get; }

    /// <summary>The manager of the tenant <see cref="T2"/>.</summary>
    public ISubscriptionManagement T2Manager { get; }

    /// <summary>The routing verifier of <see cref="TenantKey.None"/>.</summary>
    public IRoutingVerifier Verifier { get; }

    /// <summary>The routing verifier of the tenant <see cref="T2"/>.</summary>
    public IRoutingVerifier T2Verifier { get; }

    public HttpClient Client { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<EndpointHost> StartAsync(Action<SubscriptionEndpointOptions>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScenarioHost(static _ => Substitute.For<ISubscriptionStore>(), static _ => Substitute.For<IRecipientDirectory>());
        var manager = Substitute.For<ISubscriptionManagement>();
        var t2Manager = Substitute.For<ISubscriptionManagement>();
        AddBoth(builder.Services, TenantKey.None, manager);
        AddBoth(builder.Services, TenantKey.For(T2), t2Manager);
        var verifier = Substitute.For<IRoutingVerifier>();
        var t2Verifier = Substitute.For<IRoutingVerifier>();
        builder.Services.AddKeyedSingleton(TenantKey.None, verifier);
        builder.Services.AddKeyedSingleton(TenantKey.For(T2), t2Verifier);
        var app = builder.Build();
        app.Use(SignInFromHeaders);
        app.MapMsgHatchSubscriptions(configure: configure);
        await app.StartAsync(Ct);
        return new EndpointHost(app, manager, t2Manager, verifier, t2Verifier);
    }

    /// <summary>The <c>/me</c> options of the tests: the user and the tenant of the claims that the headers give.</summary>
    public static void WithSignedInUser(SubscriptionEndpointOptions options)
    {
        options.CurrentUser = static user => user.FindFirst(UserHeader)?.Value is { } id ? new UserId(id) : null;
        options.CurrentTenant = static user => user.FindFirst(TenantHeader)?.Value is { } tenant ? new TenantId(tenant) : null;
    }

    public Task<HttpResponseMessage> GetAsync(string path, string? user = null, string? tenant = null) =>
        SendAsync(HttpMethod.Get, path, body: null, user, tenant);

    public Task<HttpResponseMessage> DeleteAsync(string path, string? user = null, string? tenant = null) =>
        SendAsync(HttpMethod.Delete, path, body: null, user, tenant);

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string? body, string? user = null, string? tenant = null, string mediaType = "application/json")
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, mediaType),
        };
        AddHeader(request, UserHeader, user);
        AddHeader(request, TenantHeader, tenant);
        return Client.SendAsync(request, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>Gives the status code and the title of a problem response, for example <c>(400, "ValidationFailed")</c>.</summary>
    public static async Task<(int Status, string? Title)> ProblemOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        return ((int)response.StatusCode, problem?.Title);
    }

    /// <summary>A manager result with an error, for example for <c>Returns</c>.</summary>
    public static ValueTask<ErrorOr<T>> Failure<T>(ErrorType type) => ValueTask.FromResult(ErrorOr<T>.ForError(new Error(type, "The test failure.")));

    /// <summary>A manager result with a value.</summary>
    public static ValueTask<ErrorOr<T>> Success<T>(T value) => ValueTask.FromResult(ErrorOr<T>.ForValue(value));

    private static void AddBoth(IServiceCollection services, TenantKey key, ISubscriptionManagement management)
    {
        services.AddKeyedSingleton<ISubscriptionQueries>(key, management);
        services.AddKeyedSingleton<ISubscriptionCommands>(key, management);
    }

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (value is not null)
        {
            request.Headers.Add(name, value);
        }
    }

    private static Task SignInFromHeaders(HttpContext context, RequestDelegate next)
    {
        Claim[] claims =
        [
            .. context.Request.Headers[UserHeader].Select(static id => new Claim(UserHeader, id!)),
            .. context.Request.Headers[TenantHeader].Select(static tenant => new Claim(TenantHeader, tenant!)),
        ];
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return next(context);
    }
}
