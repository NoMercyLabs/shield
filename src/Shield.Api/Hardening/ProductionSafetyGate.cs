namespace Shield.Api.Hardening;

// Refuses to start when configuration would expose a half-secured instance to the internet.
// Runs once at startup AFTER the data-protection master-key check in Program.cs, so by the
// time we get here we know a master key was provided — we only need to validate its quality
// plus the rest of the public-exposure posture (no single-user mode, no Swagger, HTTPS
// required, JWT key length, master-key not the dev string).
//
// Each failure throws InvalidOperationException with a remediation hint so the operator can
// fix the env var without diving into source. The banner that follows lists everything that
// passed so the chosen posture is visible at a glance in the boot log.
public static class ProductionSafetyGate
{
    private const string DevDefaultMasterKey = "dev-master-key-at-least-32-chars-long-xx";
    private const int MinJwtSigningKeyLength = 48;
    private const int MinMasterKeyLength = 32;

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        bool isDevelopment = environment.IsDevelopment();
        // Tests run with environment name "Testing" — the gate is skipped there too so the
        // WebApplicationFactory tests don't need to thread a production-grade master key.
        if (isDevelopment || environment.IsEnvironment("Testing"))
            return;

        bool singleUser = configuration.GetValue("Shield:SingleUser", false);
        bool allowSingleUserInProduction = configuration.GetValue(
            "Shield:Auth:AllowSingleUserInProduction",
            false
        );
        bool openApiEnabled = configuration.GetValue("Shield:OpenApi:Enabled", false);
        bool isPublic = configuration.GetValue("Shield:Public", false);
        bool requireHttps = configuration.GetValue("Shield:Auth:RequireHttps", false);
        string? cookieDomain = configuration["Shield:Auth:CookieDomain"];

        string jwtKey =
            configuration["Shield:Auth:JwtSigningKey"]
            ?? configuration["Shield:Auth:Jwt:Secret"]
            ?? string.Empty;
        string masterKey = configuration["Shield:Auth:DataProtectionMasterKey"] ?? string.Empty;

        List<string> failures = [];

        if (singleUser && !allowSingleUserInProduction)
            failures.Add(
                "Shield:SingleUser=true outside Development is refused. "
                    + "Solo operators on the public internet expose an auto-Admin session to anyone "
                    + "who reaches the host. Either disable single-user mode (Shield__SingleUser=false) "
                    + "and register a real Admin via /api/auth/register, or explicitly accept the risk "
                    + "by setting Shield__Auth__AllowSingleUserInProduction=true (NOT recommended)."
            );

        if (openApiEnabled)
            failures.Add(
                "Shield:OpenApi:Enabled=true outside Development is refused. "
                    + "Swagger publishes every controller route and DTO shape, which is reconnaissance fuel. "
                    + "Set Shield__OpenApi__Enabled=false (or unset). Re-enable temporarily inside a "
                    + "private network if you must — never on the public internet."
            );

        if (isPublic && !requireHttps)
            failures.Add(
                "Shield:Public=true requires Shield:Auth:RequireHttps=true. "
                    + "Cookies, JWTs, and OAuth callbacks travel over the wire — refusing HTTPS on a "
                    + "public host is a credentials-leak in waiting. Terminate TLS at your reverse "
                    + "proxy (Caddy/nginx/Traefik) and set Shield__Auth__RequireHttps=true so the app "
                    + "issues Secure cookies and a Strict-Transport-Security header."
            );

        if (isPublic && string.IsNullOrWhiteSpace(cookieDomain))
            failures.Add(
                "Shield:Public=true requires Shield:Auth:CookieDomain to be set. "
                    + "Without an explicit cookie domain the auth cookie is host-only, which means a "
                    + "tunnel/proxy that rewrites the Host header (cloudflared, ngrok, oauth2-proxy "
                    + "fronts) silently breaks SPA auth. Pin the cookie scope to the tunnel hostname, "
                    + "e.g. Shield__Auth__CookieDomain=shield.example.com."
            );

        string apiTokenPepper = configuration["Shield:Auth:ApiTokenPepper"] ?? string.Empty;
        if (string.IsNullOrEmpty(apiTokenPepper))
            failures.Add(
                "Shield:Auth:ApiTokenPepper is required in non-Development environments. "
                    + "ApiTokenStore hashes the random half of every `shld_` token under this pepper — "
                    + "missing it means every token validation throws InvalidOperationException at "
                    + "first auth attempt (visible as HTTP 500, not a clear boot rejection). "
                    + "Generate one with: openssl rand -base64 48"
            );

        if (jwtKey.Length < MinJwtSigningKeyLength)
            failures.Add(
                $"Shield:Auth:JwtSigningKey must be at least {MinJwtSigningKeyLength} characters in "
                    + $"non-Development environments (currently {jwtKey.Length}). HMAC-SHA256 collapses "
                    + "below 32 bytes of entropy; 48 chars of base64 is the floor we accept here. "
                    + "Generate one with: openssl rand -base64 48"
            );

        if (masterKey.Length < MinMasterKeyLength)
            failures.Add(
                $"Shield:Auth:DataProtectionMasterKey must be at least {MinMasterKeyLength} characters "
                    + $"(currently {masterKey.Length}). The keyring under DataProtectionKeysPath is "
                    + "encrypted with this key — losing it makes every stored secret unrecoverable. "
                    + "Generate one with: openssl rand -base64 48"
            );

        if (string.Equals(masterKey, DevDefaultMasterKey, StringComparison.Ordinal))
            failures.Add(
                "Shield:Auth:DataProtectionMasterKey is the dev default ('dev-master-key-at-least-32-chars-long-xx'). "
                    + "Refusing to start because every Shield install on the internet would share this key. "
                    + "Generate a unique one with: openssl rand -base64 48"
            );

        string? oauthRedirectBase = configuration["Shield:Auth:OAuthRedirectBase"];
        if (
            requireHttps
            && !string.IsNullOrWhiteSpace(oauthRedirectBase)
            && oauthRedirectBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        )
            failures.Add(
                "Shield:Auth:OAuthRedirectBase starts with 'http://' while Shield:Auth:RequireHttps=true. "
                    + "OAuth callbacks sent over plain HTTP expose authorization codes to passive eavesdroppers. "
                    + "Update Shield__Auth__OAuthRedirectBase to use 'https://'. "
                    + "Example: Shield__Auth__OAuthRedirectBase=https://shield.example.com"
            );

        if (failures.Count > 0)
            throw new InvalidOperationException(
                "Shield refuses to start with the current configuration:"
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine + Environment.NewLine,
                        failures.Select((message, index) => $"  {index + 1}. {message}")
                    )
                    + Environment.NewLine
                    + Environment.NewLine
                    + "See docs/internet-exposure.md for the full public-exposure checklist."
            );
    }

    // Emits a one-shot banner that lists each posture knob and its current value. Operators
    // grep this in the boot log to confirm the deploy is in the expected posture.
    public static void LogPostureBanner(
        ILogger logger,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        bool singleUser = configuration.GetValue("Shield:SingleUser", false);
        bool allowSingleUserInProduction = configuration.GetValue(
            "Shield:Auth:AllowSingleUserInProduction",
            false
        );
        bool openApiEnabled = configuration.GetValue("Shield:OpenApi:Enabled", false);
        bool isPublic = configuration.GetValue("Shield:Public", false);
        bool requireHttps = configuration.GetValue("Shield:Auth:RequireHttps", false);
        string cookieDomain = configuration["Shield:Auth:CookieDomain"] ?? "(host-only)";
        string knownProxies =
            configuration["Shield:ForwardedHeaders:KnownProxies"] ?? "(loopback only)";
        string oauthRedirectBase =
            configuration["Shield:Auth:OAuthRedirectBase"] ?? "(request-derived)";

        logger.LogInformation(
            "Shield posture: Environment={Environment} Public={Public} RequireHttps={RequireHttps} "
                + "SingleUser={SingleUser} AllowSingleUserInProduction={AllowSingleUserInProduction} "
                + "OpenApi={OpenApi} CookieDomain={CookieDomain} KnownProxies={KnownProxies} "
                + "OAuthRedirectBase={OAuthRedirectBase}",
            environment.EnvironmentName,
            isPublic,
            requireHttps,
            singleUser,
            allowSingleUserInProduction,
            openApiEnabled,
            cookieDomain,
            knownProxies,
            oauthRedirectBase
        );
    }
}
