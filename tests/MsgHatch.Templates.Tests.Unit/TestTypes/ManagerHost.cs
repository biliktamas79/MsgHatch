using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>The DI container of the template manager tests, with the setup of each test (D222).</summary>
public sealed class ManagerHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public ManagerHost(Action<MsgHatchBuilder> configure)
    {
        var services = TestHost.CreateServices();
        configure(services.AddMsgHatch(static o => o.DefaultLanguage = "en"));
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    public ITemplateManager Manager(TenantKey key) => _scope.ServiceProvider.GetRequiredKeyedService<ITemplateManager>(key);

    public ITemplateEngine<OrderEmail> Engine(TenantKey key) => _scope.ServiceProvider.GetRequiredKeyedService<ITemplateEngine<OrderEmail>>(key);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
