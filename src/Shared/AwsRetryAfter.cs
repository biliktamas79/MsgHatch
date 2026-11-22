using System.Globalization;
using Amazon.Runtime.Internal;

namespace MsgHatch;

/// <summary>
/// The wait that an AWS service asked for in its error response (D306): the header <c>x-amz-retry-after</c> in
/// milliseconds, or else the header <c>Retry-After</c> in seconds, or null when the response has neither as a count.
/// The AWS SDK keeps the response of an error in the inner <see cref="HttpErrorResponseException"/>. The SNS client and
/// the SES client compile this file.
/// </summary>
internal static class AwsRetryAfter
{
    public static TimeSpan? Of(Exception exception) =>
        exception.InnerException is HttpErrorResponseException { Response: { } response }
            ? CountOf(response.GetHeaderValue("x-amz-retry-after"), TimeSpan.FromMilliseconds)
                ?? CountOf(response.GetHeaderValue("Retry-After"), TimeSpan.FromSeconds)
            : null;

    private static TimeSpan? CountOf(string? header, Func<double, TimeSpan> unit) =>
        int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? unit(count) : null;
}
