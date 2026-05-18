using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Shield.Api.Services.Auth;

// Issues Shield JWTs. Embeds the user's SecurityStamp so OnTokenValidated can revoke
// all outstanding JWTs the instant the stamp changes (password change, 2FA toggle, lockout).
public interface IJwtIssuer
{
    Task<string> IssueAsync(ShieldUser user, CancellationToken ct = default);
}

public sealed class JwtIssuer : IJwtIssuer
{
    public const string SecurityStampClaimType = "shield.security_stamp";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    private readonly SymmetricSecurityKey _signingKey;
    private readonly UserManager<ShieldUser> _userManager;

    public JwtIssuer(IConfiguration configuration, UserManager<ShieldUser> userManager)
    {
        string raw =
            configuration["Shield:Auth:JwtSigningKey"]
            ?? configuration["Shield:Auth:Jwt:Secret"]
            ?? throw new InvalidOperationException("Shield:Auth:JwtSigningKey is required.");
        _signingKey = new(Encoding.UTF8.GetBytes(raw));
        _userManager = userManager;
    }

    public async Task<string> IssueAsync(ShieldUser user, CancellationToken ct = default)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);
        string securityStamp = await _userManager.GetSecurityStampAsync(user);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.UserName ?? string.Empty),
            new(SecurityStampClaimType, securityStamp),
        ];

        foreach (string role in roles)
            claims.Add(new(ClaimTypes.Role, role));

        JwtSecurityToken token = new(
            issuer: "shield",
            audience: "shield",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: new(_signingKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
