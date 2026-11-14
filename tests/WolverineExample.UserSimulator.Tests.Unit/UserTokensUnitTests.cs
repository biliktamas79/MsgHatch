using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace WolverineExample.UserSimulator.Tests.Unit;

/// <summary>
/// The tokens of the simulated users, checked with the validation that the JWT bearer handler of the consumer applies:
/// the issuer, the audience, and the signing key of <c>dotnet user-jwts</c>. The lifetime comes from the fake clock.
/// </summary>
public sealed class UserTokensUnitTests
{
    private const string _issuer = "dotnet-user-jwts";
    private const string _audience = "http://localhost:5090";
    private const string _key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();

    [Fact]
    public async Task Create_UserOfATenant_GivesATokenWithTheUserAndTheTenantThatTheKeyOfTheConsumerValidates()
    {
        // Arrange
        await using var provider = BuildSimulator(WithUserJwtsKey(SimulatorConfiguration()));
        var user = new SimulatedUser { Tenant = "north", UserId = 1 };

        // Act
        var token = provider.GetRequiredService<UserTokens>().Create(user);

        // Assert
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, SignatureAndAudienceValidation());
        Assert.Equal(
            (true, "1", "north"),
            (result.IsValid, result.ClaimsIdentity?.FindFirst("sub")?.Value, result.ClaimsIdentity?.FindFirst("tenant")?.Value));
    }

    [Fact]
    public async Task Create_TokenLifetimeOf90Seconds_GivesATokenValidFromTheTimeOfTheClockFor90Seconds()
    {
        // Arrange
        var configuration = WithUserJwtsKey(SimulatorConfiguration());
        configuration["UserSimulator:TokenLifetime"] = "00:01:30";
        await using var provider = BuildSimulator(configuration);
        var now = _time.GetUtcNow().UtcDateTime;

        // Act
        var token = new JsonWebToken(provider.GetRequiredService<UserTokens>().Create(new SimulatedUser { Tenant = "north", UserId = 1 }));

        // Assert
        Assert.Equal((now, now.AddSeconds(90)), (token.ValidFrom, token.ValidTo));
    }

    [Fact]
    public async Task Create_NoTokenLifetimeInTheConfiguration_GivesATokenValidForOneHour()
    {
        // Arrange
        await using var provider = BuildSimulator(WithUserJwtsKey(SimulatorConfiguration()));
        var now = _time.GetUtcNow().UtcDateTime;

        // Act
        var token = new JsonWebToken(provider.GetRequiredService<UserTokens>().Create(new SimulatedUser { Tenant = "north", UserId = 1 }));

        // Assert
        Assert.Equal((now, now.AddHours(1)), (token.ValidFrom, token.ValidTo));
    }

    [Fact]
    public async Task AddUserSimulator_NoSigningKeyOfUserJwts_ThrowsOptionsValidationExceptionAtTheFirstToken()
    {
        // Arrange
        await using var provider = BuildSimulator(SimulatorConfiguration()); // invalid: dotnet user-jwts wrote no key

        // Act
        var resolve = () => provider.GetRequiredService<UserTokens>();

        // Assert
        Assert.Throws<OptionsValidationException>(resolve);
    }

    private ServiceProvider BuildSimulator(Dictionary<string, string?> configuration)
    {
        var services = TestHost.CreateServices(_time, configuration);
        services.AddUserSimulator();
        return services.Build();
    }

    private static Dictionary<string, string?> SimulatorConfiguration() => new()
    {
        ["UserSimulator:HubUrl"] = "http://localhost:5090/hubs/notifications",
        ["UserSimulator:Issuer"] = _issuer,
        ["UserSimulator:Audience"] = _audience,
        ["UserSimulator:Users:0:Tenant"] = "north",
        ["UserSimulator:Users:0:UserId"] = "1",
    };

    private static Dictionary<string, string?> WithUserJwtsKey(Dictionary<string, string?> configuration)
    {
        configuration["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = _issuer;
        configuration["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = _key;
        return configuration;
    }

    /// <summary>The validation of the consumer with no lifetime check, because the validation reads the real clock.</summary>
    private static TokenValidationParameters SignatureAndAudienceValidation() => new()
    {
        ValidIssuer = _issuer,
        ValidAudience = _audience,
        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(_key)),
        ValidateLifetime = false,
    };
}
