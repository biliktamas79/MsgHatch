using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Liquid.DependencyInjection;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Liquid.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the Liquid engine, as an xUnit class fixture: a substitute shared template store with each seed,
/// and a host with a Liquid registration of <see cref="OrderEmail"/> and one of <see cref="LineOrderEmail"/>. A seed
/// writes <c>{Name}</c> as <c>{{ Name }}</c>, and <c>{ShippedAt}</c> with the filter <c>format_date</c>, because
/// Liquid writes a date in its own format.
/// </summary>
public sealed partial class LiquidTemplateTestData : IEmailTemplateTestData, IAsyncDisposable
{
    public const string Prefix = "MsgHatch.Email.Liquid.Tests.Unit.TemplateFiles";

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
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsLiquidEmail("OrderEmail")))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                .WithPayload<LineOrderEmail, LineOrderLoader<OtherTestEvent>>(static p => p.AsLiquidEmail("LineOrder")));
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    /// <summary>The engine of <see cref="OrderEmail"/> for the key <see cref="TenantKey.None"/>.</summary>
    public IEmailTemplateEngine<OrderEmail> Engine => _scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine<OrderEmail>>();

    /// <summary>The engine of <see cref="LineOrderEmail"/> for the key <see cref="TenantKey.None"/>.</summary>
    public IEmailTemplateEngine<LineOrderEmail> LineEngine => _scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine<LineOrderEmail>>();

    public void Seed(MessageType type, string language, string subject, string html, string? text) =>
        SeedParts(
            new TemplateLookup(type, language, Scope: null),
            [
                new TemplatePart("subject", TemplatePartKind.PlainText, InLiquid(subject)),
                new TemplatePart("html", TemplatePartKind.Html, InLiquid(html)),
                .. text is null ? Array.Empty<TemplatePart>() : [new TemplatePart("text", TemplatePartKind.PlainText, InLiquid(text))],
            ]);

    /// <summary>Puts a template with the given parts into the store, with a new key or the given key.</summary>
    public void SeedParts(TemplateLookup lookup, TemplatePart[] parts, TemplateKey? key = null) =>
        _templates[lookup] = new TemplateSource(key ?? new TemplateKey(Guid.NewGuid().ToString(), 0), parts);

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
