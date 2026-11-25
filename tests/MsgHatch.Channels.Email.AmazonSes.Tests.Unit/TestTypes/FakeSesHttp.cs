using System.Net;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;

namespace MsgHatch.Channels.Email.AmazonSes.Tests.Unit.TestTypes;

/// <summary>
/// A real SES client of the AWS SDK on a fake HTTP handler. Each request gets the same error response, so a test sees
/// the exception that the SDK makes from a real response, with its headers. The client makes no retry of its own, so a
/// test does not wait for the back-off of the SDK. No request leaves the process.
/// </summary>
public static class FakeSesHttp
{
    public static AmazonSimpleEmailServiceV2Client ClientThatAnswers(
        HttpStatusCode status, string errorType, IReadOnlyDictionary<string, string> headers)
    {
        var config = new AmazonSimpleEmailServiceV2Config
        {
            RegionEndpoint = RegionEndpoint.EUWest1,
            MaxErrorRetry = 0,
            HttpClientFactory = new Factory(new Handler(status, errorType, headers)),
        };
        return new AmazonSimpleEmailServiceV2Client(new BasicAWSCredentials("AKIDEXAMPLE", "secret"), config);
    }

    private sealed class Factory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(HttpStatusCode status, string errorType, IReadOnlyDictionary<string, string> headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"message":"Too many requests."}""", Encoding.UTF8, "application/json"),
            };
            response.Headers.TryAddWithoutValidation("x-amzn-ErrorType", errorType);
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        }
    }
}
