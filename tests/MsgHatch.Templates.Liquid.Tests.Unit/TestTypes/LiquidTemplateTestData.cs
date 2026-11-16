using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Templates;
using MsgHatch.Templates.Liquid;
using MsgHatch.Templates.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Liquid.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the Liquid engine, as an xUnit class fixture: a substitute shared template store with each seed,
/// and a host with a Liquid registration of <see cref="OrderEmail"/> and one of <see cref="LineOrderEmail"/>. A seed
/// writes <c>{Name}</c> as <c>{{ Name }}</c>, and <c>{ShippedAt}</c> with the filter <c>format_date</c>, because
/// Liquid writes a date in its own format.
/// </summary>
public sealed partial class LiquidTemplateTestData : ITemplateTestData, IAsyncDisposable
{
    public const string Prefix = "MsgHatch.Templates.Liquid.Tests.Unit.TemplateFiles";

    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource> _templates = new();
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public LiquidTemplateTestData()
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(_templates.GetValueOrDefault(call.ArgAt<TemplateLookup>(0)))));
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseTemplateStore(_ => store)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail", LiquidTemplates.Engine)))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                .WithPayload<LineOrderEmail, LineOrderLoader<OtherTestEvent>>(static p => p.AsEmail("LineOrder", LiquidTemplates.Engine)));
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    /// <summary>The engine of <see cref="OrderEmail"/> for the key <see cref="TenantKey.None"/>.</summary>
    public ITemplateEngine<OrderEmail> Engine => _scope.ServiceProvider.GetRequiredService<ITemplateEngine<OrderEmail>>();

    /// <summary>The engine of <see cref="LineOrderEmail"/> for the key <see cref="TenantKey.None"/>.</summary>
    public ITemplateEngine<LineOrderEmail> LineEngine => _scope.ServiceProvider.GetRequiredService<ITemplateEngine<LineOrderEmail>>();

    public void Seed(MessageType type, string language, string part, TemplatePartKind kind, string text) =>
        SeedText(new TemplateLookup(type, language, Scope: null, part, kind), InLiquid(text));

    /// <summary>Puts a text with no change into the store, with a new key or the given key.</summary>
    public void SeedText(TemplateLookup lookup, string text, TemplateKey? key = null) =>
        _templates[lookup] = new TemplateSource(key ?? new TemplateKey(Guid.NewGuid().ToString(), 0), text);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private static string InLiquid(string text) =>
        Placeholder().Replace(text, "{{ $1 }}").Replace("{{ ShippedAt }}", "{{ ShippedAt | format_date: 'G' }}", StringComparison.Ordinal);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
