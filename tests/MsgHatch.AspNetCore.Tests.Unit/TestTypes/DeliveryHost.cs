using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit.TestTypes;

/// <summary>
/// A web application on a <see cref="TestServer"/> with the delivery endpoints, the content types of the test types,
/// and a manager substitute for <see cref="TenantKey.None"/> and for the tenant <c>t2</c>. The overview group needs the
/// role <see cref="AdminRole"/>, and the content group the role <see cref="ContentRole"/>. The header
/// <see cref="RolesHeader"/> gives the roles of the caller.
/// </summary>
public sealed class DeliveryHost : IAsyncDisposable
{
    public const string RolesHeader = "X-Test-Roles";
    public const string AdminRole = "DeliveryAdmin";
    public const string ContentRole = "DeliveryContent";

    public static readonly TenantId T2 = new("t2");

    private readonly WebApplication _app;

    private DeliveryHost(WebApplication app, IDeliveryManager manager, IDeliveryManager t2Manager)
    {
        _app = app;
        Manager = manager;
        T2Manager = t2Manager;
        Client = app.GetTestClient();
        Client.DefaultRequestHeaders.Add(RolesHeader, $"{AdminRole},{ContentRole}");
    }

    /// <summary>The manager of <see cref="TenantKey.None"/>.</summary>
    public IDeliveryManager Manager { get; }

    /// <summary>The manager of the tenant <see cref="T2"/>.</summary>
    public IDeliveryManager T2Manager { get; }

    /// <summary>A client with both roles.</summary>
    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<DeliveryHost> StartAsync(bool overview = true, bool content = true)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(TestContentTypes.ARegistry());
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores();
        var manager = Substitute.For<IDeliveryManager>();
        var t2Manager = Substitute.For<IDeliveryManager>();
        builder.Services.AddKeyedSingleton(TenantKey.None, manager);
        builder.Services.AddKeyedSingleton(TenantKey.For(T2), t2Manager);
        builder.Services.AddAuthentication(RolesHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, RolesHandler>(RolesHandler.SchemeName, null);
        builder.Services.AddAuthorization(static options =>
        {
            options.AddPolicy(AdminRole, static policy => policy.RequireRole(AdminRole));
            options.AddPolicy(ContentRole, static policy => policy.RequireRole(ContentRole));
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        MapGroups(app, overview, content);
        await app.StartAsync(Ct);
        return new DeliveryHost(app, manager, t2Manager);
    }

    public HttpClient ClientWithRoles(string roles)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(RolesHeader, roles);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync(Ct);
        await _app.DisposeAsync();
    }

    private static void MapGroups(WebApplication app, bool overview, bool content)
    {
        if (overview)
        {
            app.MapMsgHatchDelivery().RequireAuthorization(AdminRole);
        }

        if (content)
        {
            app.MapMsgHatchDeliveryContent().RequireAuthorization(ContentRole);
        }
    }

    /// <summary>Signs in a caller with the roles of the header <see cref="RolesHeader"/>, so that a test needs no token.</summary>
    private sealed class RolesHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestRoles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var roles = Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator"), .. roles.Select(static role => new Claim(ClaimTypes.Role, role))], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
