using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.Placeholders;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>
/// The DI container of the fallback tests of D123: the placeholder engine of the test event, with a substitute for
/// the shared template store and, when the test gives one, for the template store of each tenant, with no channel.
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

        builder.AddTemplateEngine<OrderEmail>("Email:OrderEmail", PlaceholderTemplates.Engine<OrderEmail>);
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    public ITemplateEngine<OrderEmail> Engine(TenantKey key) =>
        _scope.ServiceProvider.GetRequiredKeyedService<ITemplateEngine<OrderEmail>>(key);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
