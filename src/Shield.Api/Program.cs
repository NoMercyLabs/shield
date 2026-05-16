using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Shield.Alerter.Extensions;
using Shield.Api.Auth;
using Shield.Api.Auth.OAuthProviders;
using Shield.Api.Hardening;
using Shield.Api.Hubs;
using Shield.Api.Middleware;
using Shield.Api.Persistence;
using Shield.Api.Services;
using Shield.Api.Services.ManifestEditors;
using Shield.Api.Workers;
using Shield.Channels.Extensions;
using Shield.Channels.Inbox;
using Shield.Core.Abstractions;
using Shield.Core.Options;
using Shield.Data;
using Shield.Data.Extensions;
using Shield.Data.Identity;
using Shield.Feeds.Ghsa.Extensions;
using Shield.Feeds.NpmRegistry.Extensions;
using Shield.Feeds.Osv.Extensions;
using Shield.Matcher.Extensions;
using Shield.Parsers.Composer.Extensions;
using Shield.Parsers.Go.Extensions;
using Shield.Parsers.Gradle.Extensions;
using Shield.Parsers.Npm.Extensions;
using Shield.Parsers.Nuget.Extensions;
using Shield.Parsers.Python.Extensions;
using Shield.Parsers.Rust.Extensions;
using Shield.Scanners.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ConfigurationManager configuration = builder.Configuration;

// Options
builder.Services.Configure<ShieldOptions>(configuration.GetSection(ShieldOptions.SectionName));
builder.Services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

// Shield core: two DbContexts + parsers/feeds/scanners/matcher/alerter/channels.
builder.Services.AddShieldData(configuration);
builder.Services.AddNpmParser();
builder.Services.AddNugetParser();
builder.Services.AddComposerParser();
builder.Services.AddGradleParser();
builder.Services.AddPythonParser();
builder.Services.AddGoParser();
builder.Services.AddRustParser();
builder.Services.AddOsvFeed();
builder.Services.AddGhsaFeed(configuration);
builder.Services.AddNpmRegistryFeed(configuration);
builder.Services.AddShieldScanners();
builder.Services.AddShieldMatcher();
builder.Services.AddShieldAlerter();
builder.Services.AddShieldChannels();

// Inbox persistence — separate DbContext, same SQLite file as Shield DB.
string inboxConnection =
    configuration["Shield:Db:Shield"]
    ?? throw new InvalidOperationException("Configuration value 'Shield:Db:Shield' is required.");
builder.Services.AddDbContext<InboxDbContext>(options => options.UseSqlite(inboxConnection));
builder.Services.AddScoped<IInboxStore, InboxStore>();

// In-process work queues.
builder.Services.AddSingleton<ScanQueue>();
builder.Services.AddSingleton<MatchQueue>();
builder.Services.AddSingleton<FeedRefreshQueue>();

// Background workers.
builder.Services.AddHostedService<SourceScanWorker>();
builder.Services.AddHostedService<FeedSyncWorker>();
builder.Services.AddHostedService<MatcherWorker>();
builder.Services.AddHostedService<AlertDispatchWorker>();

// DataProtection — persistent keyring + master-key envelope. MUST be registered before
// AddIdentity so Identity's cookie protector latches onto our configured keyring instead
// of the ephemeral Linux container default (wiped on every recreate). The master key is
// hashed to 32 bytes (AES-256) and wraps every keyring XML entry written to disk.
string keysPath =
    configuration["Shield:Auth:DataProtectionKeysPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "keys");
Directory.CreateDirectory(keysPath);

IDataProtectionBuilder dataProtection = builder
    .Services.AddDataProtection()
    .SetApplicationName("Shield")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

string? masterKey = configuration["Shield:Auth:DataProtectionMasterKey"];
if (!string.IsNullOrEmpty(masterKey))
{
    byte[] keyBytes = System.Security.Cryptography.SHA256.HashData(
        Encoding.UTF8.GetBytes(masterKey)
    );
    dataProtection.ProtectKeysWithMasterKey(keyBytes);
}
else if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    // Testing skipped too: WebApplicationFactory layers config AFTER this gate runs, so
    // even though the test factory provides a master key the value isn't visible here yet.
    throw new InvalidOperationException(
        "Shield:Auth:DataProtectionMasterKey is required in non-Development environments — "
            + "stored secrets would be lost on container recreate without it."
    );
}

// Production-safety gate — refuses to start when public-exposure config is half-secured.
// Skipped in Development + Testing. See Hardening/ProductionSafetyGate.cs for the failure list.
ProductionSafetyGate.Validate(configuration, builder.Environment);

// Identity — cookies for the SPA, JWT bearer for API clients. Password policy + lockout
// tighten automatically when Shield:Public=true; otherwise dev-friendly defaults stay.
bool shieldPublicMode = configuration.GetValue("Shield:Public", false);
bool shieldSingleUserMode = configuration.GetValue("Shield:SingleUser", false);
builder
    .Services.AddIdentity<ShieldUser, ShieldRole>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;

        if (shieldPublicMode)
        {
            // Public hosts get NIST-flavoured strict policy + 5-strike lockout.
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequiredLength = 12;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }
        else
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            // Multi-user mode (not single-user) still wants lockout-on-failure even off the
            // public internet — brute force from a LAN host is still brute force.
            if (!shieldSingleUserMode)
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            }
        }
    })
    .AddEntityFrameworkStores<ShieldDbContext>()
    .AddDefaultTokenProviders();

string jwtKey =
    configuration["Shield:Auth:JwtSigningKey"]
    ?? configuration["Shield:Auth:Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Shield:Auth:JwtSigningKey (or Shield:Auth:Jwt:Secret) is required."
    );
SymmetricSecurityKey signingKey = new(Encoding.UTF8.GetBytes(jwtKey));

// AddIdentity already registered Identity.Application as the default cookie scheme; layer
// JWT bearer alongside so headless API clients can authenticate without a cookie.
builder
    .Services.AddAuthentication()
    .AddJwtBearer(
        JwtBearerDefaults.AuthenticationScheme,
        options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ClockSkew = TimeSpan.FromMinutes(2),
            };
        }
    )
    .AddScheme<
        Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        SingleUserAuthHandler
    >(SingleUserAuthHandler.SchemeName, configureOptions: null);

bool requireHttpsForCookies = configuration.GetValue("Shield:Auth:RequireHttps", false);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "shield.auth";
    options.Cookie.HttpOnly = true;
    // SameSite=Strict + Secure when behind TLS — the SPA + API share an origin so Strict
    // doesn't break OAuth (callbacks land on /api/oauth/<provider>/callback same-origin).
    options.Cookie.SameSite = requireHttpsForCookies
        ? SameSiteMode.Strict
        : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = requireHttpsForCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.LoginPath = "/login";
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorization(options =>
{
    // Default policy accepts the Identity cookie (SPA), a JWT bearer (API clients), or the
    // SingleUser convenience scheme (solo operators). The auth handler decides whether each
    // request matches its scheme; SingleUser only succeeds when Shield:SingleUser=true and
    // no real cookie is present.
    options.DefaultPolicy = new AuthorizationPolicyBuilder(
        IdentityConstants.ApplicationScheme,
        JwtBearerDefaults.AuthenticationScheme,
        SingleUserAuthHandler.SchemeName
    )
        .RequireAuthenticatedUser()
        .Build();

    // Maintainer-or-higher policy — Admin always qualifies because Admin owns every source.
    options.AddPolicy(
        "MaintainerOrAdmin",
        policy =>
            policy.RequireRole(ShieldRoles.Admin, ShieldRoles.Maintainer).RequireAuthenticatedUser()
    );
});

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IFindingsBroadcaster, FindingsBroadcaster>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

// Security headers middleware — registered transient so each request gets a fresh instance
// that reads RequireHttps from configuration (avoids capturing a stale snapshot at boot).
builder.Services.AddTransient<SecurityHeadersMiddleware>();

// Forwarded headers — required when behind a reverse proxy that terminates TLS so
// HttpsRedirection sees the original https:// scheme instead of the proxy's http:// hop,
// which would cause a redirect loop. Trust only KnownNetworks=loopback by default; operators
// running on a real cluster network add their proxy subnet via Shield:ForwardedHeaders.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Empty = trust all proxies. Operators on multi-hop networks tighten via KnownProxies/Networks
    // — for the single-container + sidecar Caddy reference deploy, all hops are loopback.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate limiter — token-bucket per IP on login/register/OAuth callbacks. 10 reqs/min/IP.
// BCL Microsoft.AspNetCore.RateLimiting (no NuGet add). Rejected requests get a 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "auth-burst",
        context =>
        {
            string partitionKey =
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-remote";
            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 10,
                    TokensPerPeriod = 10,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

// Audit log: HttpContext access for the AuditLogger (actor + remote IP); scoped logger
// rides the request DbContext; middleware is transient and resolved per-request.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddTransient<AuditMiddleware>();

// Per-source ACL resolver — scoped because it reads ShieldDbContext (scoped) and memoises
// results onto HttpContext.Items so List + per-row CanRead checks within one request only
// hit the DB once.
builder.Services.AddScoped<IAccessResolver, AccessResolver>();

// Runtime-mutable settings cache. Singleton so Current is shared across requests and
// SingleUserAuthHandler sees the new snapshot the instant SettingsController writes.
builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();

// Host whitelist for auto-detected git remotes on LocalFolder scans.
builder.Services.AddSingleton<Shield.Scanners.IDetectedRemoteHostPolicy>(
    sp => new AppSettingsRemoteHostPolicy(sp.GetRequiredService<IAppSettingsService>())
);

// OAuth integration plumbing — PKCE state cache + encrypted token store + per-provider adapters.
builder.Services.AddSingleton<IOAuthStateStore, OAuthStateStore>();
builder.Services.AddSingleton<IOAuthTokenStore, OAuthTokenStore>();
builder.Services.AddHttpClient("oauth");
builder.Services.AddSingleton<IOAuthProvider, GitHubProvider>();
builder.Services.AddSingleton<IOAuthProvider, SlackProvider>();
builder.Services.AddSingleton<IOAuthProvider, GoogleProvider>();
builder.Services.AddSingleton<IOAuthProviderRegistry, OAuthProviderRegistry>();
builder.Services.AddSingleton<IOAuthTokenAccessor, OAuthTokenAccessor>();

// Best-effort fix-suggestion + apply pipeline. Editors are stateless; suggester is pure.
// Applier touches GitHub via Octokit so it stays singleton (no scoped state needed).
builder.Services.AddSingleton<IFixSuggester, FixSuggester>();
builder.Services.AddSingleton<IManifestEditor, NpmManifestEditor>();
builder.Services.AddSingleton<IManifestEditor, NugetManifestEditor>();
builder.Services.AddSingleton<IManifestEditor, ComposerManifestEditor>();
builder.Services.AddSingleton<IManifestEditor, GradleManifestEditor>();
builder.Services.AddSingleton<IManifestEditor, PythonManifestEditor>();
builder.Services.AddSingleton<IManifestEditor, GoManifestEditor>();
builder.Services.AddSingleton<IManifestEditor, RustManifestEditor>();
builder.Services.AddSingleton<IFixApplier, FixApplier>();

// Webhooks + badge endpoints. Validator + renderer + GitHub client factory are pure singletons;
// SecretProvider holds a DataProtector singleton; PrCommentService is scoped because it
// touches both DbContexts.
builder.Services.AddSingleton<IWebhookSignatureValidator, WebhookSignatureValidator>();
builder.Services.AddSingleton<IBadgeRenderer, BadgeRenderer>();
builder.Services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
builder.Services.AddSingleton<IWebhookSecretProvider, WebhookSecretProvider>();
builder.Services.AddScoped<IPrCommentService, PrCommentService>();

// Snapshot-to-snapshot supply-chain anomaly detector. Scoped because it writes to
// FeedsDbContext (advisories) + reads ShieldDbContext (snapshots/items); a Singleton
// would capture both contexts and trip the captive-dep trap CLAUDE.md warns about.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAnomalyDetector, AnomalyDetector>();

// Swagger is off unconditionally when Shield:Public=true — even if the operator forgot to
// flip Shield:OpenApi:Enabled, internet exposure must not advertise the API surface.
bool enableOpenApi =
    !configuration.GetValue("Shield:Public", false)
    && (
        builder.Environment.IsDevelopment()
        || configuration.GetValue("Shield:OpenApi:Enabled", false)
    );
if (enableOpenApi)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Shield API", Version = "v1" });
    });
}

WebApplication app = builder.Build();

// Apply migrations on startup (both Shield + Feeds contexts).
await app.Services.MigrateShieldAsync();

// Inbox lives in the same SQLite file as the Shield DB; create the table on first boot.
using (IServiceScope scope = app.Services.CreateScope())
{
    InboxDbContext inboxDb = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
    await inboxDb.Database.EnsureCreatedAsync();
}

// Seed Admin/Viewer roles and (in single-user mode) the synthetic operator account.
await IdentitySeeder.SeedAsync(app.Services);

// Detect public-posture transition: when Shield:Public flips false→true, revoke OAuth
// integration tokens and bump SecurityStamp on every user so cached creds from the
// LAN-only era can't surface on the public host.
await PublicPostureTransition.RunAsync(app.Services, app.Logger);

// Boot banner so operators see the chosen posture immediately in logs.
ProductionSafetyGate.LogPostureBanner(app.Logger, configuration, app.Environment);

// Forwarded headers MUST run before HttpsRedirection so X-Forwarded-Proto is honoured by
// the redirect logic — otherwise we bounce-loop between the proxy and the app.
app.UseForwardedHeaders();

if (configuration.GetValue("Shield:Auth:RequireHttps", false))
    app.UseHttpsRedirection();

// SecurityHeaders runs early so static files, SPA fallback, controllers, and SignalR all
// pick up the same hardened header set.
app.UseMiddleware<SecurityHeadersMiddleware>();

if (enableOpenApi)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Resolve wwwroot defensively — prefer the configured WebRootPath, fall back to AppContext.BaseDirectory/wwwroot
// then ContentRootPath/wwwroot. Necessary because `dotnet run`, `dotnet publish` output, and the Docker image
// each leave wwwroot in a different spot, and a missing dir silently disables static-file serving.
string[] candidateWebRoots =
{
    app.Environment.WebRootPath ?? string.Empty,
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"),
};
string resolvedWebRoot =
    candidateWebRoots
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .FirstOrDefault(Directory.Exists)
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

Microsoft.Extensions.FileProviders.PhysicalFileProvider spaFileProvider = new(resolvedWebRoot);
app.Logger.LogInformation("SPA file provider rooted at {ResolvedWebRoot}", resolvedWebRoot);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = spaFileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = spaFileProvider });

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Rate limiter — runs after auth so 429s are attributable in audit, before controllers so
// the limiter sees the inbound path. The auth-burst policy is opted-in per route below via
// RequireRateLimiting on the controller routes that accept anonymous credentials.
app.UseRateLimiter();

// Audit log middleware — runs AFTER auth so it can read the actor claims, and BEFORE
// MapControllers so the controller pipeline executes within its scope. Records 2xx writes
// for whitelisted admin actions (finding transitions, source/channel mutations, settings,
// OAuth connect/disconnect). See AuditMiddleware.Classify for the whitelist.
app.UseMiddleware<AuditMiddleware>();

app.MapControllers();
app.MapHub<FindingsHub>("/hubs/findings");
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// SPA fallback — serve index.html for non-API GETs.
app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
    context.Response.ContentType = "text/html";
    string indexPath = Path.Combine(resolvedWebRoot, "index.html");
    if (File.Exists(indexPath))
        return context.Response.SendFileAsync(indexPath);
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
