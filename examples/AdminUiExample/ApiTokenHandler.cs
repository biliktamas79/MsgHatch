using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace AdminUiExample;

/// <summary>
/// Adds the token of the configuration to each request of the API clients (D235). The example has no sign-in, so the token
/// is the same for each user of the UI. A host with a sign-in reads the token of the signed-in user (section 4.23).
/// </summary>
/// <param name="options">The settings of the example.</param>
public sealed class ApiTokenHandler(IOptions<AdminUiExampleOptions> options) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiToken);
        return base.SendAsync(request, cancellationToken);
    }
}
