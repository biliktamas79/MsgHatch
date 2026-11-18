using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Api.Client.DependencyInjection;

/// <summary>
/// The setup methods of the HTTP client of the subscription management API (D227, D231, section 4.23). Each method selects
/// the HTTP client in place of the in-process default, as a scoped tenant service with its logging wrapper, through
/// <see cref="SubscriptionExtensions.UseSubscriptionQueries"/>, <see cref="SubscriptionExtensions.UseRoutingVerifier"/>, or
/// <see cref="SubscriptionExtensions.UseSubscriptionCommands"/>. So a second selection of one interface gives a setup error.
/// The options bind from the section <c>MsgHatch:SubscriptionApiClient</c>, a value from code wins, and an options error
/// stops the start of the host. The host adds its own handlers, for example the authentication, with <c>configureHttp</c>.
/// The client calls the routes with no <c>/me</c> (D154), and does not retry a create.
/// </summary>
public static class SubscriptionApiClientExtensions
{
    private const string _queriesClient = "MsgHatch.SubscriptionApi.Queries";
    private const string _commandsClient = "MsgHatch.SubscriptionApi.Commands";

    /// <summary>Selects the HTTP client for <see cref="ISubscriptionQueries"/>, <see cref="IRoutingVerifier"/>, and <see cref="ISubscriptionCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of each HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionApiClient(
        this MsgHatchBuilder builder, Action<SubscriptionApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null) =>
        SelectCommands(
            SelectQueries(builder, configure, configureHttp, nameof(UseSubscriptionApiClient)), configure, configureHttp, nameof(UseSubscriptionApiClient));

    /// <summary>Selects the HTTP client of the API at a base address for the queries, the verifier, and the commands.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of each HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null) =>
        builder.UseSubscriptionApiClient(BaseAddressOf(baseAddress), configureHttp);

    /// <summary>
    /// Selects the HTTP client for <see cref="ISubscriptionQueries"/> and <see cref="IRoutingVerifier"/> only, so that the
    /// host can write in process. A verification changes no stored subscription, so it goes with the queries (D232).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionQueriesApiClient(
        this MsgHatchBuilder builder, Action<SubscriptionApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null) =>
        SelectQueries(builder, configure, configureHttp, nameof(UseSubscriptionQueriesApiClient));

    /// <summary>Selects the HTTP client of the API at a base address for the queries and the verifier only.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionQueriesApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null) =>
        builder.UseSubscriptionQueriesApiClient(BaseAddressOf(baseAddress), configureHttp);

    /// <summary>Selects the HTTP client for <see cref="ISubscriptionCommands"/> only, so that the host can read in process.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionCommandsApiClient(
        this MsgHatchBuilder builder, Action<SubscriptionApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null) =>
        SelectCommands(builder, configure, configureHttp, nameof(UseSubscriptionCommandsApiClient));

    /// <summary>Selects the HTTP client of the API at a base address for <see cref="ISubscriptionCommands"/> only.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseSubscriptionCommandsApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null) =>
        builder.UseSubscriptionCommandsApiClient(BaseAddressOf(baseAddress), configureHttp);

    private static MsgHatchBuilder SelectQueries(
        MsgHatchBuilder builder, Action<SubscriptionApiClientOptions>? configure, Action<IHttpClientBuilder>? configureHttp, string setupMethod)
    {
        AddHttpClient(builder, _queriesClient, configure, configureHttp);
        return builder
            .UseSubscriptionQueries(static (services, key) => new HttpSubscriptionQueries(ApiOf(services, _queriesClient), key, RegistryOf(services)), setupMethod)
            .UseRoutingVerifier(static (services, key) => new HttpRoutingVerifier(ApiOf(services, _queriesClient), key, RegistryOf(services)), setupMethod);
    }

    private static MsgHatchBuilder SelectCommands(
        MsgHatchBuilder builder, Action<SubscriptionApiClientOptions>? configure, Action<IHttpClientBuilder>? configureHttp, string setupMethod)
    {
        AddHttpClient(builder, _commandsClient, configure, configureHttp);
        return builder.UseSubscriptionCommands(
            static (services, key) => new HttpSubscriptionCommands(ApiOf(services, _commandsClient), key, RegistryOf(services)), setupMethod);
    }

    private static void AddHttpClient(
        MsgHatchBuilder builder, string name, Action<SubscriptionApiClientOptions>? configure, Action<IHttpClientBuilder>? configureHttp)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<SubscriptionApiClientOptions>().BindConfiguration(SubscriptionApiClientOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SubscriptionApiClientOptions>, SubscriptionApiClientOptionsValidator>());
        var http = builder.Services.AddHttpClient(name, static (services, client) =>
        {
            var settings = services.GetRequiredService<IOptions<SubscriptionApiClientOptions>>().Value;
            client.BaseAddress = WithTrailingSlash(settings.BaseAddress!);
            client.Timeout = settings.Timeout;
        });
        configureHttp?.Invoke(http);
    }

    private static SubscriptionApi ApiOf(IServiceProvider services, string name) =>
        new(services.GetRequiredService<IHttpClientFactory>().CreateClient(name), services.GetRequiredService<IOptions<SubscriptionApiClientOptions>>().Value.Prefix);

    private static IContentTypeRegistry RegistryOf(IServiceProvider services) => services.GetRequiredService<IContentTypeRegistry>();

    private static Action<SubscriptionApiClientOptions> BaseAddressOf(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return options => options.BaseAddress = baseAddress;
    }

    private static Uri WithTrailingSlash(Uri baseAddress) =>
        baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
}
