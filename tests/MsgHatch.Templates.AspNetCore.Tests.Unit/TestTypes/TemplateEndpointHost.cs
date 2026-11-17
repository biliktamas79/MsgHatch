using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;
using NSubstitute;

namespace MsgHatch.Templates.AspNetCore.Tests.Unit.TestTypes;

/// <summary>
/// A web application on a <see cref="TestServer"/> with the template endpoints, and a manager substitute for
/// <see cref="TenantKey.None"/> and for the tenant <c>t2</c>. Each substitute gives the entry of a save with the version
/// 7, the text <c>Order of {{Name}}</c> with the version 5 for a read, two entries for a list, and no error for a delete.
/// </summary>
public sealed class TemplateEndpointHost : IAsyncDisposable
{
    public static readonly TenantId T2 = new("t2");

    private readonly WebApplication _app;

    private TemplateEndpointHost(WebApplication app, ITemplateManager manager, ITemplateManager t2Manager)
    {
        _app = app;
        Manager = manager;
        T2Manager = t2Manager;
        Client = app.GetTestClient();
    }

    /// <summary>The manager of <see cref="TenantKey.None"/>.</summary>
    public ITemplateManager Manager { get; }

    /// <summary>The manager of the tenant <see cref="T2"/>.</summary>
    public ITemplateManager T2Manager { get; }

    public HttpClient Client { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<TemplateEndpointHost> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var manager = AManager();
        var t2Manager = AManager();
        builder.Services.AddKeyedSingleton(TenantKey.None, manager);
        builder.Services.AddKeyedSingleton(TenantKey.For(T2), t2Manager);
        var app = builder.Build();
        app.MapMsgHatchTemplates();
        await app.StartAsync(Ct);
        return new TemplateEndpointHost(app, manager, t2Manager);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync(Ct);
        await _app.DisposeAsync();
    }

    private static ITemplateManager AManager()
    {
        var manager = Substitute.For<ITemplateManager>();
        manager.SaveTemplateAsync(default, default!, default, default).ReturnsForAnyArgs(static call =>
            ValueTask.FromResult(ErrorOr<TemplateEntry>.ForValue(new TemplateEntry(call.ArgAt<TemplateLookup>(0), 7))));
        manager.GetTemplateAsync(default, default).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<TemplateSource>.ForValue(new TemplateSource(new TemplateKey("C:\\host\\templates\\A.en.subject.txt", 5), "Order of {{Name}}"))));
        manager.GetTemplatesAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForValue(
        [
            new TemplateEntry(new TemplateLookup(new MessageType("A"), "en", Scope: null, "subject", TemplatePartKind.PlainText), 5),
            new TemplateEntry(new TemplateLookup(new MessageType("A"), "hu", "42", "body", TemplatePartKind.Html), 6),
        ])));
        manager.DeleteTemplateAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        return manager;
    }
}
