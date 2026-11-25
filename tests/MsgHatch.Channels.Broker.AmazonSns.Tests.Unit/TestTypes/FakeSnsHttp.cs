using System.Net;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;

namespace MsgHatch.Channels.Broker.AmazonSns.Tests.Unit.TestTypes;

/// <summary>
/// A real SNS client of the AWS SDK on a fake HTTP handler. Each request gets the same error response, so a test sees
/// the exception that the SDK makes from a real response, with its headers. No request leaves the process.
/// </summary>
public static class FakeSnsHttp
{
    public static AmazonSimpleNotificationServiceClient ClientThatAnswers(
        HttpStatusCode status, string errorCode, IReadOnlyDictionary<string, string> headers)
    {
        var config = new AmazonSimpleNotificationServiceConfig
        {
            RegionEndpoint = RegionEndpoint.EUWest1,
            HttpClientFactory = new Factory(new Handler(status, errorCode, headers)),
        };
        return new AmazonSimpleNotificationServiceClient(new BasicAWSCredentials("AKIDEXAMPLE", "secret"), config);
    }

    private sealed class Factory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(HttpStatusCode status, string errorCode, IReadOnlyDictionary<string, string> headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = $"""<ErrorResponse xmlns="http://sns.amazonaws.com/doc/2010-03-31/"><Error><Type>Sender</Type><Code>{errorCode}</Code><Message>Rate exceeded</Message></Error><RequestId>request-1</RequestId></ErrorResponse>""";
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return Task.FromResult(response);
        }
    }
}
