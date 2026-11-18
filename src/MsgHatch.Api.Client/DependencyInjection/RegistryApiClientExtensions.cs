using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Client.DependencyInjection;

/// <summary>
/// The setup methods of the HTTP client of the registry API (D271, D283, section 4.23). Each method selects the HTTP client
/// in place of the in-process defaults of <see cref="IRegistryQueries"/> and <see cref="IRegistryCommands"/>, as scoped
/// tenant services with their logging wrappers, through <see cref="MsgHatchBuilder.UseRegistryQueries"/> and
/// <see cref="MsgHatchBuilder.UseRegistryCommands"/>. So a second selection gives a setup error. The options bind
/// from the section <c>MsgHatch:RegistryApiClient</c>, a value from code wins, and an options error stops the start of the
/// host. The host adds its own handlers, for example the authentication, with <c>configureHttp</c>.
/// </summary>
public static class RegistryApiClientExtensions
{
    private const string _queriesClient = "MsgHatch.RegistryApi.Queries";

    /// <summary>Selects the HTTP client for <see cref="IRegistryQueries"/> and <see cref="IRegistryCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRegistryApiClient(
        this MsgHatchBuilder builder, Action<RegistryApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<RegistryApiClientOptions>().BindConfiguration(RegistryApiClientOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<RegistryApiClientOptions>, RegistryApiClientOptionsValidator>());
        var http = builder.Services.AddHttpClient(_queriesClient, static (services, client) =>
        {
            var settings = services.GetRequiredService<IOptions<RegistryApiClientOptions>>().Value;
            client.BaseAddress = WithTrailingSlash(settings.BaseAddress!);
            client.Timeout = settings.Timeout;
        });
        configureHttp?.Invoke(http);
        return builder
            .UseRegistryQueries(static (services, _) => new HttpRegistryQueries(ApiOf(services)), nameof(UseRegistryApiClient))
            .UseRegistryCommands(static (services, _) => new HttpRegistryCommands(ApiOf(services)), nameof(UseRegistryApiClient));
    }

    /// <summary>Selects the HTTP client of the API at a base address for <see cref="IRegistryQueries"/> and <see cref="IRegistryCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseRegistryApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return builder.UseRegistryApiClient(options => options.BaseAddress = baseAddress, configureHttp);
    }

    private static RegistryApi ApiOf(IServiceProvider services) =>
        new(services.GetRequiredService<IHttpClientFactory>().CreateClient(_queriesClient), services.GetRequiredService<IOptions<RegistryApiClientOptions>>().Value.Prefix);

    private static Uri WithTrailingSlash(Uri baseAddress) =>
        baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");
}
