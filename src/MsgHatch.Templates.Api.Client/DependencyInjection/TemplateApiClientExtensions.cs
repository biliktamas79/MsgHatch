using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Api.Client.DependencyInjection;

/// <summary>
/// The setup methods of the HTTP client of the template management API (D227, D231, section 4.23). Each method selects the
/// HTTP client in place of the in-process default, as a scoped tenant service with its logging wrapper, through
/// <see cref="TemplateExtensions.UseTemplateQueries"/> or <see cref="TemplateExtensions.UseTemplateCommands"/>. So a second
/// selection of one interface gives a setup error. The options bind from the section <c>MsgHatch:TemplateApiClient</c>, a
/// value from code wins, and an options error stops the start of the host. The host adds its own handlers, for example the
/// authentication, with <c>configureHttp</c>.
/// </summary>
public static class TemplateApiClientExtensions
{
    private const string _queriesClient = "MsgHatch.TemplateApi.Queries";
    private const string _commandsClient = "MsgHatch.TemplateApi.Commands";

    /// <summary>Selects the HTTP client for <see cref="ITemplateQueries"/> and <see cref="ITemplateCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of each HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateApiClient(
        this MsgHatchBuilder builder, Action<TemplateApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null) =>
        SelectCommands(SelectQueries(builder, configure, configureHttp, nameof(UseTemplateApiClient)), configure, configureHttp, nameof(UseTemplateApiClient));

    /// <summary>Selects the HTTP client of the API at a base address for <see cref="ITemplateQueries"/> and <see cref="ITemplateCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of each HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null) =>
        builder.UseTemplateApiClient(BaseAddressOf(baseAddress), configureHttp);

    /// <summary>Selects the HTTP client for <see cref="ITemplateQueries"/> only, so that the host can write in process.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateQueriesApiClient(
        this MsgHatchBuilder builder, Action<TemplateApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null) =>
        SelectQueries(builder, configure, configureHttp, nameof(UseTemplateQueriesApiClient));

    /// <summary>Selects the HTTP client of the API at a base address for <see cref="ITemplateQueries"/> only.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateQueriesApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null) =>
        builder.UseTemplateQueriesApiClient(BaseAddressOf(baseAddress), configureHttp);

    /// <summary>Selects the HTTP client for <see cref="ITemplateCommands"/> only, so that the host can read in process.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateCommandsApiClient(
        this MsgHatchBuilder builder, Action<TemplateApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null) =>
        SelectCommands(builder, configure, configureHttp, nameof(UseTemplateCommandsApiClient));

    /// <summary>Selects the HTTP client of the API at a base address for <see cref="ITemplateCommands"/> only.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseTemplateCommandsApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null) =>
        builder.UseTemplateCommandsApiClient(BaseAddressOf(baseAddress), configureHttp);

    private static MsgHatchBuilder SelectQueries(
        MsgHatchBuilder builder, Action<TemplateApiClientOptions>? configure, Action<IHttpClientBuilder>? configureHttp, string setupMethod)
    {
        AddHttpClient(builder, _queriesClient, configure, configureHttp);
        return builder.UseTemplateQueries(static (services, key) => new HttpTemplateQueries(ApiOf(services, _queriesClient), key), setupMethod);
    }

    private static MsgHatchBuilder SelectCommands(
        MsgHatchBuilder builder, Action<TemplateApiClientOptions>? configure, Action<IHttpClientBuilder>? configureHttp, string setupMethod)
    {
        AddHttpClient(builder, _commandsClient, configure, configureHttp);
        return builder.UseTemplateCommands(static (services, key) => new HttpTemplateCommands(ApiOf(services, _commandsClient), key), setupMethod);
    }

    private static void AddHttpClient(
        MsgHatchBuilder builder, string name, Action<TemplateApiClientOptions>? configure, Action<IHttpClientBuilder>? configureHttp)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<TemplateApiClientOptions>().BindConfiguration(TemplateApiClientOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TemplateApiClientOptions>, TemplateApiClientOptionsValidator>());
        var http = builder.Services.AddHttpClient(name, static (services, client) =>
        {
            var settings = services.GetRequiredService<IOptions<TemplateApiClientOptions>>().Value;
            client.BaseAddress = WithTrailingSlash(settings.BaseAddress!);
            client.Timeout = settings.Timeout;
        });
        configureHttp?.Invoke(http);
    }

    private static TemplateApi ApiOf(IServiceProvider services, string name) =>
        new(services.GetRequiredService<IHttpClientFactory>().CreateClient(name), services.GetRequiredService<IOptions<TemplateApiClientOptions>>().Value.Prefix);

    private static Action<TemplateApiClientOptions> BaseAddressOf(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return options => options.BaseAddress = baseAddress;
    }

    private static Uri WithTrailingSlash(Uri baseAddress) =>
        baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
}
