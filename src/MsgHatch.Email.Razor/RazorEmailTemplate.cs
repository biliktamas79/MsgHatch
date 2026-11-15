using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Razor;

/// <summary>
/// One prepared template of the Razor engine (D202): the subject and the text of the template store, and the html of a
/// component. Each render makes a new <see cref="HtmlRenderer"/>, because a renderer keeps each rendered component until
/// it is disposed. The render sets the culture of the route in the dispatcher, so the culture of the caller stays.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal sealed class RazorEmailTemplate<TPayload>(
    IEmailTemplate<TPayload> subjectAndText, Type component, IServiceProvider services, ILoggerFactory loggerFactory)
    : IEmailTemplate<TPayload>
{
    public async ValueTask<ErrorOr<RenderedEmail>> RenderAsync(TPayload payload, MessageRoute route, CultureInfo culture, CancellationToken ct)
    {
        var parts = await subjectAndText.RenderAsync(payload, route, culture, ct);
        if (parts.IsError)
        {
            return parts.Error;
        }

        ct.ThrowIfCancellationRequested();
        return parts.Value with { HtmlBody = await RenderHtmlAsync(payload, culture) };
    }

    private async Task<string> RenderHtmlAsync(TPayload payload, CultureInfo culture)
    {
        await using var renderer = new HtmlRenderer(new RenderServices(services), loggerFactory);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(RazorEmailComponent<TPayload>.Payload)] = payload });
            var output = await renderer.RenderComponentAsync(component, parameters);
            return output.ToHtmlString();
        });
    }

    /// <summary>
    /// The services of a renderer: the <see cref="HtmlEncoder"/> of the placeholder renderer, which keeps accented letters
    /// (section 4.10), and each other service from the tenant services of the engine.
    /// </summary>
    // ponytail: no keyed services for a component, add IKeyedServiceProvider when a template needs [Inject(Key = ...)]
    private sealed class RenderServices(IServiceProvider services) : IServiceProvider
    {
        private static readonly HtmlEncoder _encoder = HtmlEncoder.Create(UnicodeRanges.All);

        public object? GetService(Type serviceType) => serviceType == typeof(HtmlEncoder) ? _encoder : services.GetService(serviceType);
    }
}
