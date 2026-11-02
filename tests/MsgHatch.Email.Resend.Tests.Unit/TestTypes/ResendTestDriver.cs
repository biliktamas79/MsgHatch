using System.Net;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Tests.Unit.Contracts;

namespace MsgHatch.Email.Resend.Tests.Unit.TestTypes;

/// <summary>The driver of the Resend client tests: <see cref="FakeResendApi"/> as the provider.</summary>
public sealed class ResendTestDriver : IEmailClientTestDriver
{
    public FakeResendApi Api { get; } = new();

    public void Accepts(string providerMessageId) =>
        Api.RespondsWith(HttpStatusCode.OK, $$"""{"id":"{{providerMessageId}}"}""");

    public void Fails(ErrorType type) =>
        Api.RespondsWith((HttpStatusCode)StatusOf(type), FakeResendApi.ErrorBody(StatusOf(type), NameOf(type), "The request failed."));

    public void WaitsUntilCancelled() => Api.WaitsUntilCancelled();

    public void CanSend() => Api.RespondsWith(HttpStatusCode.OK, """{"object":"list","data":[]}""");

    public void CannotSend() =>
        Api.RespondsWith(HttpStatusCode.Forbidden, FakeResendApi.ErrorBody(403, "suspended_api_key", "This API key is suspended"));

    private static int StatusOf(ErrorType type) =>
        type == Errors.Rejected ? 422
        : type == Errors.Unavailable ? 503
        : throw new ArgumentOutOfRangeException(nameof(type), type.Name, "The driver has no Resend response for this error type.");

    private static string NameOf(ErrorType type) => type == Errors.Rejected ? "missing_required_field" : "service_unavailable";
}
