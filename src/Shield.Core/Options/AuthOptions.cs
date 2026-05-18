namespace Shield.Core.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Shield:Auth";

    public JwtOptions Jwt { get; set; } = new();
    public OidcOptions Oidc { get; set; } = new();
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "shield";
    public string Audience { get; set; } = "shield";
    public string Secret { get; set; } = string.Empty;
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}

public sealed class OidcOptions
{
    public bool Enabled { get; set; }
    public string? Issuer { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RoleClaim { get; set; }
}
