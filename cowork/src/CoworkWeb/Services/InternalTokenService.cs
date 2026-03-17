using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoworkWeb.Services;

/// <summary>
/// Issues short-lived (5-minute) signed JWTs for Blazor-to-Node.js API calls.
/// The Node.js CoworkAgent validates these with the same shared secret.
/// </summary>
public sealed class InternalTokenService
{
    private readonly string _secret;
    private readonly SigningCredentials _signingCredentials;

    public InternalTokenService(IConfiguration config)
    {
        _secret = config["CoworkAgent:InternalSecret"]
            ?? throw new InvalidOperationException("CoworkAgent:InternalSecret not configured");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string Issue(string userId, string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:   "cowork-web",
            audience: "cowork-agent",
            claims:   claims,
            notBefore: DateTime.UtcNow,
            expires:   DateTime.UtcNow.AddMinutes(5),
            signingCredentials: _signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
