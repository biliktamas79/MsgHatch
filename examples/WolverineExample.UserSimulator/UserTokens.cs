using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WolverineExample.UserSimulator;

/// <summary>
/// Signs a JWT for a simulated user with the key of <c>dotnet user-jwts</c>, as
/// <c>dotnet user-jwts create --name {user} --claim tenant={tenant}</c> does. The hub of the consumer reads the user from
/// the claim <c>sub</c> and the tenant from the claim <c>tenant</c>. Only a development setup has this key. A real
/// client gets its token from an identity provider.
/// </summary>
public sealed class UserTokens
{
    /// <summary>The claim type of the tenant, which the hub of the consumer reads.</summary>
    public const string TenantClaim = "tenant";

    private readonly JsonWebTokenHandler _handler = new();
    private readonly UserSimulatorOptions _settings;
    private readonly SigningCredentials _credentials;
    private readonly TimeProvider _time;

    /// <summary>Selects the signing key of the issuer of the settings.</summary>
    /// <param name="settings">The settings, with the issuer, the audience, and the token lifetime.</param>
    /// <param name="keys">The signing keys of <c>dotnet user-jwts</c>.</param>
    /// <param name="time">The clock of the token times.</param>
    /// <exception cref="InvalidOperationException">No signing key has the issuer of the settings.</exception>
    public UserTokens(IOptions<UserSimulatorOptions> settings, IOptions<UserJwtsOptions> keys, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(keys);
        _settings = settings.Value;
        _credentials = CredentialsOf(keys.Value.SigningKeys, _settings.Issuer);
        _time = time;
    }

    /// <summary>Makes a token for one user, valid from now for <see cref="UserSimulatorOptions.TokenLifetime"/>.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The signed token.</returns>
    public string Create(SimulatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = _time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.UserId.ToString(CultureInfo.InvariantCulture),
                [TenantClaim] = user.Tenant,
            },
            NotBefore = now,
            IssuedAt = now,
            Expires = now + _settings.TokenLifetime,
            SigningCredentials = _credentials,
        });
    }

    private static SigningCredentials CredentialsOf(List<UserJwtsSigningKey> keys, string issuer)
    {
        var key = keys.Find(key => key.Issuer == issuer)
            ?? throw new InvalidOperationException($"No signing key of dotnet user-jwts has the issuer {issuer}.");
        return new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(key.Value)), SecurityAlgorithms.HmacSha256);
    }
}
