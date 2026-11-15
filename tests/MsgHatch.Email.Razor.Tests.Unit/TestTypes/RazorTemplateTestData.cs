using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Razor.DependencyInjection;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Razor.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the Razor engine, as an xUnit class fixture: a substitute shared template store with the subject and
/// the text of each seed, and a host with the Razor templates of the folder <c>EmailTemplates</c> of this project. The
/// html of a seed is a component of that folder with the name of its message type, so a seed puts only the subject and
/// the text into the store.
/// </summary>
public sealed partial class RazorTemplateTestData : IEmailTemplateTestData, IAsyncDisposable
{
    public const string Root = "MsgHatch.Email.Razor.Tests.Unit.EmailTemplates";

    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource> _templates = new();
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public RazorTemplateTestData()
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(_templates.GetValueOrDefault(call.ArgAt<TemplateLookup>(0)))));
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseTemplateStore(_ => store)
            .UseRazorEmailTemplates(typeof(RazorTemplateTestData).Assembly, Root)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsRazorEmail("OrderEmail")));
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    /// <summary>The engine of <see cref="OrderEmail"/> for the key <see cref="TenantKey.None"/>.</summary>
    public IEmailTemplateEngine<OrderEmail> Engine => _scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine<OrderEmail>>();

    public void Seed(MessageType type, string language, string subject, string html, string? text) =>
        SeedParts(
            new TemplateLookup(type, language, Scope: null),
            [
                new TemplatePart("subject", TemplatePartKind.PlainText, InPlaceholderSyntax(subject)),
                .. text is null ? Array.Empty<TemplatePart>() : [new TemplatePart("text", TemplatePartKind.PlainText, InPlaceholderSyntax(text))],
            ]);

    /// <summary>Puts a subject into the store, or nothing for a null subject.</summary>
    public void SeedSubject(MessageType type, string language, string? scope, string? subject)
    {
        if (subject is not null)
        {
            SeedParts(new TemplateLookup(type, language, scope), [new TemplatePart("subject", TemplatePartKind.PlainText, subject)]);
        }
    }

    public void SeedParts(TemplateLookup lookup, TemplatePart[] parts) =>
        _templates[lookup] = new TemplateSource(new TemplateKey(Guid.NewGuid().ToString(), 0), parts);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private static string InPlaceholderSyntax(string text) => Placeholder().Replace(text, "{{$1}}");

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
