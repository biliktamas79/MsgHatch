using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SignalRDemo.Sessions;

/// <summary>
/// Signs and checks the tokens of the demo login with a random key that the app makes at each start, so the demo needs
/// no setup and keeps no secret in a file. A token does not survive a restart.
/// </summary>
/// <param name="time">The clock of the token times.</param>
public sealed class DemoTokens(TimeProvider time)
{
    /// <summary>The issuer and the audience of the tokens.</summary>
    public const string Issuer = "SignalRDemo";

    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(8);
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SymmetricSecurityKey _key = new(RandomNumberGenerator.GetBytes(32));

    /// <summary>Makes a token for one account, valid from now for 8 hours.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The signed token.</returns>
    public string Create(DemoAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var now = time.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = account.User.Value,
                [DemoAccount.TenantClaim] = account.Tenant.Value,
                [DemoAccount.CompanyClaim] = account.Company.Value,
            },
            NotBefore = now,
            IssuedAt = now,
            Expires = now + _lifetime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Sets the JWT bearer handler to check the tokens of this class, with the claim names of the token.</summary>
    /// <param name="options">The options of the JWT bearer handler.</param>
    public void Configure(JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Issuer,
            IssuerSigningKey = _key,
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
        options.Events = new JwtBearerEvents { OnMessageReceived = ReadTheQueryTokenOfTheHub };
    }

    /// <summary>
    /// A browser sends no header with a WebSocket, so the JavaScript client of SignalR sends the token in the query
    /// <c>access_token</c>.
    /// </summary>
    private static Task ReadTheQueryTokenOfTheHub(MessageReceivedContext context)
    {
        if (context.Request.Path.StartsWithSegments(DemoHub.Route, StringComparison.Ordinal)
            && context.Request.Query["access_token"] is [{ Length: > 0 } token])
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }
}
