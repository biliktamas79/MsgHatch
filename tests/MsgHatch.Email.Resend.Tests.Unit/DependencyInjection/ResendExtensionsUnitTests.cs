using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Email.Resend.DependencyInjection;
using MsgHatch.Email.Resend.Tests.Unit.TestTypes;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Resend.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseResend</c> alone registers all parts of the package (section 4.7, D60). Each test puts <see cref="FakeResendApi"/>
/// under the HTTP client of the package, so no request leaves the process.
/// </summary>
public sealed class ResendExtensionsUnitTests : IDisposable
{
    private const string _apiKey = "re_test_123";
    private const string _senderEmail = "noreply@example.com";

    private readonly FakeResendApi _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string?, string?, string> MissingOptionRows => new()
    {
        { null, _senderEmail, nameof(ResendOptions.ApiKey) },
        { _apiKey, null, nameof(ResendOptions.SenderEmail) },
    };

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task UseResend_Alone_ResolvesTheEmailClientAsTheLoggingWrapperAroundTheResendClient()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseResend(static o => { o.ApiKey = _apiKey; o.SenderEmail = _senderEmail; });
        await using var provider = WithTheFakeApi(services).Build();
        var client = provider.GetRequiredService<IEmailClient>();

        // Act
        await client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.IsNotType<ResendEmailClient>(client);
        Assert.Single(provider.LogRecords(), static record => record.Id.Id == 2004);
        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task UseResend_Alone_RegistersTheTransportOfTheEmailChannel()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseResend(static o => { o.ApiKey = _apiKey; o.SenderEmail = _senderEmail; });
        await using var provider = WithTheFakeApi(services).Build();

        // Act
        var transport = Assert.Single(provider.GetServices<ITransport>());

        // Assert
        Assert.Equal(EmailChannel.Id, transport.Channel);
    }

    [Fact]
    public async Task UseResend_OptionsInTheConfiguration_SendsWithTheApiKeyAndFromTheSenderOfTheConfiguration()
    {
        // Arrange
        var configuration = new Dictionary<string, string?> { ["MsgHatch:Resend:ApiKey"] = "re_config", ["MsgHatch:Resend:SenderEmail"] = "config@example.com" };
        var services = TestHost.CreateServices(configuration: configuration);
        services.AddMsgHatch().UseResend();
        await using var provider = WithTheFakeApi(services).Build();

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        var request = Assert.Single(_api.Requests);
        Assert.Equal(
            ($"Bearer {configuration["MsgHatch:Resend:ApiKey"]}", configuration["MsgHatch:Resend:SenderEmail"]),
            (request.Header("Authorization"), request.Json?["from"]?.GetValue<string>()));
    }

    [Fact]
    public async Task UseResend_SenderEmailInCodeAndInTheConfiguration_SendsFromTheAddressOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:Resend:SenderEmail"] = "config@example.com" });
        services.AddMsgHatch().UseResend(static o => { o.ApiKey = _apiKey; o.SenderEmail = _senderEmail; });
        await using var provider = WithTheFakeApi(services).Build();

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(_senderEmail, Assert.Single(_api.Requests).Json?["from"]?.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(MissingOptionRows))]
    public async Task Validate_MissingRequiredOption_ThrowsOptionsValidationExceptionForThatOption(string? apiKey, string? senderEmail, string expectedProperty)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseResend(o => { o.ApiKey = apiKey!; o.SenderEmail = senderEmail!; });
        await using var provider = WithTheFakeApi(services).Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        var failure = Assert.Single(validation.Failures);
        Assert.Equal((typeof(ResendOptions), expectedProperty), (validation.OptionsType, failure[..failure.IndexOf(':', StringComparison.Ordinal)]));
    }

    private IServiceCollection WithTheFakeApi(IServiceCollection services)
    {
        services.AddHttpClient(ResendEmailClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _api);
        return services;
    }
}
