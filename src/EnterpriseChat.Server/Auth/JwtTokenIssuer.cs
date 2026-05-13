using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EnterpriseChat.Server.Auth;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken Issue(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Name, user.Username),
            new Claim("full_name", user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedToken(encoded, expiresAt);
    }
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
