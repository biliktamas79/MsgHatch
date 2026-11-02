using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// The DI container of the fallback tests of D123: the placeholder engine of the test event, with a substitute for
/// the shared template store and, when the test gives one, for the template store of each tenant.
/// </summary>
public sealed class EngineHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public EngineHost(ITemplateStore? tenantStore, ITemplateStore sharedStore)
    {
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch(static o => o.DefaultLanguage = "en").UseTemplateStore(_ => sharedStore);
        if (tenantStore is not null)
        {
            builder.UseTenantTemplateStore((_, _) => tenantStore);
        }

        builder.AddMessage("TestEvent", 1, TestJsonContext.Default.TestEvent, static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    public IEmailTemplateEngine<OrderEmail> Engine(TenantKey key) =>
        _scope.ServiceProvider.GetRequiredKeyedService<IEmailTemplateEngine<OrderEmail>>(key);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
