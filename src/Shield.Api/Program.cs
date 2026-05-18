using System.Net;
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
using Shield.Api.Auth.AcceptanceTickets;
using Shield.Api.Auth.External;
using Shield.Api.Auth.OAuthProviders;
using Shield.Api.Hardening;
using Shield.Api.Http;
using Shield.Api.Hubs;
using Shield.Api.Middleware;
using Shield.Api.Persistence;
using Shield.Api.Services;
using Shield.Api.Services.BulkFix;
using Shield.Api.Services.Ecosystems;
using Shield.Api.Services.FixApply;
using Shield.Api.Services.ManifestEditors;
using Shield.Api.Services.PullRequests;
using Shield.Api.Services.SourceFs;
using Shield.Api.Services.Updates;
using Shield.Api.Workers;
using Shield.Api.Workers.Queues;
using Shield.Channels.Extensions;
using Shield.Channels.Inbox;
using Shield.Channels.Smtp;
using Shield.Core.Abstractions;
using Shield.Core.Options;
using Shield.Data;
using Shield.Data.Extensions;
using Shield.Data.Identity;
using Shield.Feeds.Extensions;
using Shield.Matcher.Extensions;
using Shield.Parsers.Extensions;
using Shield.Scanners.Extensions;
using INotificationPublisher = Shield.Core.Abstractions.INotificationPublisher;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ConfigurationManager configuration = builder.Configuration;

// Options
builder.Services.Configure<ShieldOptions>(configuration.GetSection(ShieldOptions.SectionName));
builder.Services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));

// Shield core: two DbContexts + parsers/feeds/scanners/matcher/alerter/channels.
builder.Services.AddShieldData(configuration);
builder.Services.AddShieldParsers();
builder.Services.AddScoped<IKevAdvisoryEnricher, EfKevAdvisoryEnricher>();
builder.Services.AddScoped<IEpssAdvisoryEnricher, EfEpssAdvisoryEnricher>();
builder.Services.AddShieldFeeds(configuration);
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

// In-process work queues. ScanQueue stays registered for the scheduler/legacy in-memory path;
// new on-demand enqueues go through IPersistentScanQueue → ScanQueueEntries table.
builder.Services.AddSingleton<ScanQueue>();
builder.Services.AddSingleton<MatchQueue>();
builder.Services.AddSingleton<FeedRefreshQueue>();
builder.Services.AddScoped<IPersistentScanQueue, PersistentScanQueue>();

// Background workers.
builder.Services.AddHostedService<SourceScanWorker>();
builder.Services.AddHostedService<ScanQueueWorker>();
builder.Services.AddHostedService<FeedSyncWorker>();
builder.Services.AddHostedService<MatcherWorker>();
builder.Services.AddHostedService<AlertDispatchWorker>();

builder.Services.AddShieldUpdates();

builder.Services.AddShieldEcosystems();

// Notification publisher is Scoped (writes to ShieldDbContext); the OAuth-expiry watcher
// is a hosted Singleton that wakes hourly and resolves the publisher from a fresh scope.
// Registered as Core.Abstractions.INotificationPublisher so Shield.Channels (InboxChannel)
// can resolve it without a circular dependency on Shield.Api.
builder.Services.AddScoped<INotificationPublisher, NotificationPublisher>();

// Security event logger — scoped. Writes SecurityEvent rows + upserts IpReputation +
// broadcasts a `security.event` SignalR frame for the in-app Security view.
builder.Services.AddScoped<ISecurityEventLogger, SecurityEventLogger>();

// Session auditor — centralises post-signin plumbing (audit + notification + security event).
builder.Services.AddScoped<ISessionAuditor, SessionAuditor>();

// Admin audience provider — lets Channels resolve admin user IDs without Identity dep.
builder.Services.AddScoped<IAdminAudienceProvider, AdminAudienceProvider>();

// Web Push sender — singleton. Reads/writes VAPID via IAppSettingsService (singleton),
// opens a per-dispatch scope for PushSubscription rows. WebPushClient is stateless so
// one-per-call is the cheap path.
builder.Services.AddSingleton<IWebPushSender, WebPushSender>();
builder.Services.AddSingleton<OauthExpiryWatcher>();
builder.Services.AddSingleton<IOauthExpiryWatcher>(sp =>
    sp.GetRequiredService<OauthExpiryWatcher>()
);
builder.Services.AddHostedService(sp => sp.GetRequiredService<OauthExpiryWatcher>());

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
    .PersistKeysToFileSystem(new(keysPath));

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
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = "shield",
                ValidateAudience = true,
                ValidAudience = "shield",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ClockSkew = TimeSpan.FromMinutes(2),
            };
            // Revoke every outstanding JWT the instant the user's SecurityStamp changes
            // (password change, 2FA toggle, lockout). No blocklist needed.
            options.Events = new()
            {
                OnTokenValidated = async ctx =>
                {
                    string? stampInToken = ctx
                        .Principal?.Claims.FirstOrDefault(c =>
                            c.Type == JwtIssuer.SecurityStampClaimType
                        )
                        ?.Value;
                    string? rawUserId = ctx
                        .Principal?.Claims.FirstOrDefault(c =>
                            c.Type == System.Security.Claims.ClaimTypes.NameIdentifier
                            || c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub
                        )
                        ?.Value;
                    if (stampInToken is null || rawUserId is null)
                    {
                        ctx.Fail("JWT is missing required claims.");
                        return;
                    }
                    var um = ctx.HttpContext.RequestServices.GetRequiredService<
                        UserManager<Shield.Data.Identity.ShieldUser>
                    >();
                    var user = await um.FindByIdAsync(rawUserId);
                    if (user is null)
                    {
                        ctx.Fail("User not found.");
                        return;
                    }
                    string currentStamp = await um.GetSecurityStampAsync(user);
                    if (!string.Equals(stampInToken, currentStamp, StringComparison.Ordinal))
                        ctx.Fail("Security stamp mismatch — token revoked.");
                },
            };
        }
    )
    .AddScheme<
        Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
        SingleUserAuthHandler
    >(SingleUserAuthHandler.SchemeName, configureOptions: null)
    .AddScheme<ApiTokenAuthOptions, ApiTokenAuthHandler>(
        ApiTokenAuthHandler.SchemeName,
        configureOptions: null
    );

bool requireHttpsForCookies = configuration.GetValue("Shield:Auth:RequireHttps", false);
string? cookieDomain = configuration["Shield:Auth:CookieDomain"];
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "shield.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    // SameSite=Strict + Secure when behind TLS — the SPA + API share an origin so Strict
    // doesn't break OAuth (callbacks land on /api/oauth/<provider>/callback same-origin).
    options.Cookie.SameSite = requireHttpsForCookies ? SameSiteMode.Strict : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = requireHttpsForCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    // Cookie.Domain is host-only by default. Operators serving Shield over a tunnel hostname
    // (cloudflared, ngrok) pin the cookie to that hostname so a sibling subdomain on the same
    // apex can't read it.
    if (!string.IsNullOrWhiteSpace(cookieDomain))
        options.Cookie.Domain = cookieDomain;
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
        SingleUserAuthHandler.SchemeName,
        ApiTokenAuthHandler.SchemeName
    )
        .RequireAuthenticatedUser()
        .Build();

    // Role-based policies — MUST list every authentication scheme explicitly. `[Authorize(Roles=X)]`
    // ignores DefaultPolicy.AuthenticationSchemes and falls back to DefaultAuthenticateScheme
    // (Identity.Application), so SingleUser / JWT / ApiToken never get a chance to authenticate
    // the request. We declare the schemes here once and route the controllers through the named
    // policies instead.
    string[] allSchemes =
    [
        IdentityConstants.ApplicationScheme,
        JwtBearerDefaults.AuthenticationScheme,
        SingleUserAuthHandler.SchemeName,
        ApiTokenAuthHandler.SchemeName,
    ];
    options.AddPolicy(
        ShieldPolicies.Admin,
        policy =>
        {
            policy.AuthenticationSchemes = allSchemes;
            policy.RequireAuthenticatedUser().RequireRole(ShieldRoles.Admin);
        }
    );
    options.AddPolicy(
        "MaintainerOrAdmin",
        policy =>
        {
            policy.AuthenticationSchemes = allSchemes;
            policy
                .RequireAuthenticatedUser()
                .RequireRole(ShieldRoles.Admin, ShieldRoles.Maintainer);
        }
    );
});

// Antiforgery — XSRF-TOKEN cookie (readable by SPA, HttpOnly=false) + X-XSRF-TOKEN header.
// CookieAuthCsrfFilter (registered globally below) only enforces for cookie-auth requests;
// JWT / ApiToken / SingleUser requests carry no cookie that a malicious page could replay.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = requireHttpsForCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllers(options =>
    options.Filters.Add<Shield.Api.Auth.CookieAuthCsrfFilter>()
);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IFindingsBroadcaster, FindingsBroadcaster>();
builder.Services.AddHttpClient();

// Bind the canonical UA on EVERY named/typed client (current + future) at registration time.
// Beats hunting down individual AddHttpClient calls when adding new integrations.
builder.Services.ConfigureHttpClientDefaults(b => b.AddShieldUserAgent());
builder.Services.AddMemoryCache();

// GitHub rate-limit handler — wraps every outbound api.github.com call (REST + Octokit) so
// per-installation token budgets are honoured. Store is singleton, handler is transient so
// each HttpClient pipeline gets its own DelegatingHandler instance (DelegatingHandler tracks
// InnerHandler so reuse across pipelines is a classic ASP.NET footgun).
builder.Services.AddSingleton<GitHubRateLimitStore>();
builder.Services.AddTransient<GitHubRateLimitHandler>();
builder
    .Services.AddHttpClient(
        "github",
        client =>
        {
            client.BaseAddress = new("https://api.github.com/");
            client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        }
    )
    .AddHttpMessageHandler<GitHubRateLimitHandler>();

// Security headers middleware — registered transient so each request gets a fresh instance
// that reads RequireHttps from configuration (avoids capturing a stale snapshot at boot).
builder.Services.AddTransient<SecurityHeadersMiddleware>();

// HSTS — only meaningful when RequireHttps=true (otherwise UseHsts is skipped below).
// Public hosts get a year + includeSubDomains so a tunnel apex covers any sibling alias
// the operator points at the same backend. Preload is intentionally omitted: submission to
// the preload list is a manual humans-in-the-loop step Shield can't claim on the operator's
// behalf, and undoing a preload entry takes months.
bool isPublicForHsts = configuration.GetValue("Shield:Public", false);
builder.Services.AddHsts(options =>
{
    options.MaxAge = isPublicForHsts ? TimeSpan.FromDays(365) : TimeSpan.FromDays(30);
    options.IncludeSubDomains = isPublicForHsts;
    options.Preload = false;
});

// Forwarded headers — required when behind a reverse proxy that terminates TLS so
// HttpsRedirection sees the original https:// scheme instead of the proxy's http:// hop,
// which would cause a redirect loop. The middleware only rewrites RemoteIp/Scheme when
// the immediate hop is in KnownProxies / KnownNetworks; anything else is rejected silently
// (the header survives but RemoteIp stays the real connection IP, so audit logs can't be
// spoofed by a malicious client adding their own X-Forwarded-For).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // ASP.NET Core seeds loopback into KnownIPNetworks by default; clear then re-add explicitly
    // so the trusted set is the exact union of {loopback, operator-configured proxies}.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);

    string? configuredProxies = configuration["Shield:ForwardedHeaders:KnownProxies"];
    if (!string.IsNullOrWhiteSpace(configuredProxies))
    {
        foreach (
            string entry in configuredProxies.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (IPAddress.TryParse(entry, out IPAddress? parsed))
                options.KnownProxies.Add(parsed);
        }
    }

    string? configuredNetworks = configuration["Shield:ForwardedHeaders:KnownNetworks"];
    if (!string.IsNullOrWhiteSpace(configuredNetworks))
    {
        foreach (
            string entry in configuredNetworks.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            int slashIndex = entry.IndexOf('/');
            if (slashIndex < 0)
                continue;
            string addressPart = entry[..slashIndex];
            string prefixPart = entry[(slashIndex + 1)..];
            if (
                IPAddress.TryParse(addressPart, out IPAddress? networkAddress)
                && int.TryParse(prefixPart, out int prefixLength)
            )
            {
                options.KnownIPNetworks.Add(new(networkAddress, prefixLength));
            }
        }
    }

    // Cap chain length: a public client should never legitimately produce more than one proxy
    // hop (cloudflared/caddy) ahead of Shield. ForwardLimit defaults to 1 in ASP.NET Core but
    // setting it explicitly makes the intent reviewable and survives framework default changes.
    options.ForwardLimit = configuration.GetValue("Shield:ForwardedHeaders:ForwardLimit", 2);
});

// Rate limiter — token-bucket per IP on login/register/OAuth callbacks. 10 reqs/min/IP.
// BCL Microsoft.AspNetCore.RateLimiting (no NuGet add). Rejected requests get a 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        ISecurityEventLogger? securityLog =
            context.HttpContext.RequestServices.GetService<ISecurityEventLogger>();
        if (securityLog is null)
            return;
        try
        {
            await securityLog.LogAsync(
                source: "shield.ratelimit",
                eventType: "rate.limit",
                severity: Shield.Core.Domain.Severity.Low,
                remoteIp: context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                userAgent: context.HttpContext.Request.Headers.UserAgent.ToString()
                    is { Length: > 0 } ua
                    ? ua
                    : null,
                path: context.HttpContext.Request.Path.Value,
                ct: ct
            );
        }
        catch
        {
            // Observation failure must not promote the 429 to something worse.
        }
    };
    options.AddPolicy(
        "auth-burst",
        context =>
        {
            string partitionKey =
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-remote";
            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ =>
                    new()
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

    // External-auth flow buckets: bot-grade aggression. /start kicks off a device-code/invite
    // round-trip (expensive) so it's the tighter bucket; /poll is cheap but called every couple
    // of seconds by the SPA, so it gets a bigger budget. Both partition per IP — a legit user
    // hitting their own retry from a clean network won't notice; a scraper sweeping the surface
    // gets 429'd inside the first minute. Sibling endpoints (auth/external/*, accept-invite)
    // opt in via [EnableRateLimiting("auth-external")].
    options.AddPolicy(
        "auth-external",
        context =>
        {
            string partitionKey =
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-remote";
            bool isPoll =
                context.Request.Path.Value?.Contains("/poll", StringComparison.OrdinalIgnoreCase)
                == true;
            int tokenLimit = isPoll ? 60 : 10;
            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ =>
                    new()
                    {
                        TokenLimit = tokenLimit,
                        TokensPerPeriod = tokenLimit,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }
            );
        }
    );

    // Bulk-apply — one PR per source per call; 10/IP/hour prevents accidental spam.
    // OnRejected writes a security event so operators see the throttle in the Security view.
    options.AddPolicy(
        "bulk-apply",
        context =>
        {
            string partitionKey =
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-remote";
            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ =>
                    new()
                    {
                        TokenLimit = 10,
                        TokensPerPeriod = 10,
                        ReplenishmentPeriod = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    }
            );
        }
    );

    // fail2ban ingest — bursts during an active attack are expected, but a single legit host
    // shouldn't sustain more than ~10 ban events/second. Token bucket with a 600/min budget +
    // 10-per-second refill matches "burst-then-quiet" cleanly without 429'ing real attacks.
    options.AddPolicy(
        "fail2ban-ingest",
        context =>
        {
            string partitionKey =
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown-remote";
            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey,
                _ =>
                    new()
                    {
                        TokenLimit = 600,
                        TokensPerPeriod = 600,
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

// Personal API tokens (Shield-equivalent of GitHub PATs). Scoped — owns a DbContext + writes
// LastUsedAt on auth. Pepper rotates separately from password hashes via Shield:Auth:ApiTokenPepper.
builder.Services.AddScoped<IApiTokenStore, ApiTokenStore>();

// Invite + acceptance-ticket plumbing. AcceptanceTicketService is a singleton because it only
// reads the JWT signing key from configuration. SmtpSender is the plain BCL wrapper (no auth
// state). InviteEmailSender is scoped — it reads enabled SMTP AlertChannels from the request
// DbContext to pick configuration.
builder.Services.AddSingleton<IAcceptanceTicketService, AcceptanceTicketService>();
builder.Services.AddSingleton<ISmtpSender, SystemNetSmtpSender>();
builder.Services.AddScoped<IInviteEmailSender, InviteEmailSender>();

// Per-source ACL resolver — scoped because it reads ShieldDbContext (scoped) and memoises
// results onto HttpContext.Items so List + per-row CanRead checks within one request only
// hit the DB once.
builder.Services.AddScoped<IAccessResolver, AccessResolver>();

// GitHub-derived access layer — projects each user's GitHub org memberships onto Shield's
// per-source ACL so collaborators see their team's repos without an admin pre-seeding rows.
// Singleton — owns its own ConcurrentDictionary cache; opens a scoped DbContext per refresh.
builder.Services.AddSingleton<IGithubAccessResolver, GithubAccessResolver>();

// Runtime-mutable settings cache. Singleton so Current is shared across requests and
// SingleUserAuthHandler sees the new snapshot the instant SettingsController writes.
builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();

// 2FA enforcement + session tracking. TwoFactorEnforcement is a thin facade over the
// settings service (singleton-safe). SessionTracker holds a scoped DbContext so it MUST be
// scoped — captive-dep trap CLAUDE.md warns about. The middleware classes resolve their
// scoped deps from HttpContext.RequestServices.
builder.Services.AddSingleton<ITwoFactorEnforcement, TwoFactorEnforcement>();
builder.Services.AddScoped<ISessionTracker, SessionTracker>();
builder.Services.AddScoped<ISessionCookieIssuer, SessionCookieIssuer>();
builder.Services.AddScoped<IJwtIssuer, JwtIssuer>();
builder.Services.AddSingleton<IImpersonationCookieIssuer, ImpersonationCookieIssuer>();
builder.Services.AddTransient<TwoFactorEnforcementMiddleware>();
builder.Services.AddTransient<SessionTrackingMiddleware>();
builder.Services.AddTransient<ImpersonationMiddleware>();

// Identity's SecurityStampValidator re-hits the user store on this interval and bumps the
// cookie's identity if the stored stamp changed (password rotated, 2FA toggled, role removed).
// Default is 30m which is too lax for a security-critical app — tighten to 1m so a password
// change or 2FA disable kicks sibling browsers within the next minute. Cost: one DB hit per
// active session per minute. Negligible at Shield's scale.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(1);
});

// Host whitelist for auto-detected git remotes on LocalFolder scans.
builder.Services.AddSingleton<Shield.Scanners.IDetectedRemoteHostPolicy>(
    sp => new AppSettingsRemoteHostPolicy(sp.GetRequiredService<IAppSettingsService>())
);

// OAuth integration plumbing — PKCE state cache + encrypted token store + per-provider adapters.
builder.Services.AddSingleton<IOAuthStateStore, OAuthStateStore>();
builder.Services.AddSingleton<IOAuthTokenStore, OAuthTokenStore>();
builder.Services.AddHttpClient("oauth");
builder.Services.AddHttpClient("oidc-test");
builder.Services.AddSingleton<IOAuthProvider, GitHubProvider>();
builder.Services.AddSingleton<IOAuthProvider, SlackProvider>();
builder.Services.AddSingleton<IOAuthProvider, GoogleProvider>();
builder.Services.AddSingleton<IOAuthProviderRegistry, OAuthProviderRegistry>();
builder.Services.AddSingleton<IOAuthTokenAccessor, OAuthTokenAccessor>();

// GitHub collaborator directory — reads orgs/members/search using the admin's connect-flow
// OAuth token. Singleton: IHttpClientFactory + IMemoryCache + IOAuthTokenStore are all
// singletons themselves, so no scoped DbContext is captured here.
builder.Services.AddSingleton<IGithubCollaboratorDirectory, GithubCollaboratorDirectory>();

// GitHub device-flow plumbing — public client_id ships baked-in so self-hosted users skip
// the OAuth-App registration song-and-dance. Client talks to github.com; store maps the
// SPA-facing flowId to the upstream device_code (memory-cache, 15-min TTL).
builder.Services.AddSingleton<IGitHubDeviceFlowClient, GitHubDeviceFlowClient>();
builder.Services.AddSingleton<IGitHubDeviceFlowStore, GitHubDeviceFlowStore>();

// External-login (signin-flow) substrate. Provider-agnostic by design: the controller +
// SPA card render any IExternalLoginProvider registered here. Today: GitHub via the same
// device-flow client the connect-flow already uses. Tomorrow: GitLab / Bitbucket / Gitea /
// Forgejo arrive as additional AddSingleton<IExternalLoginProvider, …>() lines, no
// rewrites elsewhere.
builder.Services.AddSingleton<IExternalLoginFlowStore, ExternalLoginFlowStore>();
builder.Services.AddSingleton<IExternalLoginProvider, GithubExternalLoginProvider>();
builder.Services.AddSingleton<IExternalLoginProviderRegistry, ExternalLoginProviderRegistry>();

builder.Services.AddShieldFixApply();

// Webhooks + badge endpoints. Validator + renderer + GitHub client factory are pure singletons;
// SecretProvider holds a DataProtector singleton; PrCommentService is scoped because it
// touches both DbContexts.
builder.Services.AddSingleton<IWebhookSignatureValidator, WebhookSignatureValidator>();
builder.Services.AddSingleton<IBadgeRenderer, BadgeRenderer>();
builder.Services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
builder.Services.AddSingleton<IWebhookSecretProvider, WebhookSecretProvider>();
builder.Services.AddScoped<IPrCommentService, PrCommentService>();

// Replace the anonymous scanner-client factory registered by AddShieldScanners with the
// token-aware production version that runs through the GitHubRateLimitHandler.
builder.Services.AddSingleton<
    Shield.Scanners.IGitHubScannerClientFactory,
    AuthenticatedGitHubScannerClientFactory
>();

// Snapshot-to-snapshot supply-chain anomaly detector. Scoped because it writes to
// FeedsDbContext (advisories) + reads ShieldDbContext (snapshots/items); a Singleton
// would capture both contexts and trip the captive-dep trap CLAUDE.md warns about.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPopularPackageRegistry, PopularPackageRegistry>();
builder.Services.AddSingleton<ITyposquatDetector, TyposquatDetector>();
builder.Services.AddScoped<IAnomalyDetector, AnomalyDetector>();

// region OG (link-preview / Open Graph)
// Image renderer is a Singleton — the embedded Inter typeface loads once and SkiaSharp's
// SKCanvas usage here is stateless per-call (each Render allocates its own SKBitmap).
builder.Services.AddSingleton<IOgImageRenderer, OgImageRenderer>();

// Crawler middleware reads the SPA index.html via the same physical file provider the
// static-file pipeline uses. Provider is registered below (after webroot resolution)
// because the path depends on runtime layout.
builder.Services.AddTransient<Shield.Api.Middleware.CrawlerMetaMiddleware>();

// endregion OG

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
        options.SwaggerDoc(
            "v1",
            new()
            {
                Title = "Shield API",
                Version = "v1",
                Description =
                    "Self-hosted dependency vulnerability watcher. "
                    + "Supports cookie session, JWT bearer, and `shld_`-prefixed API tokens.",
                License = new()
                {
                    Name = "MIT",
                    Url = new("https://github.com/nomercylabs/shield/blob/master/LICENSE"),
                },
            }
        );

        // API token / JWT bearer (Authorization: Bearer <token>). The same header carries both —
        // ApiTokenAuthHandler matches the `shld_` prefix, JwtBearer takes anything else.
        options.AddSecurityDefinition(
            "Bearer",
            new()
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "shld_xxx or JWT",
                Description =
                    "API tokens have prefix `shld_` (see /account/tokens). JWTs come from /api/auth/login.",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            }
        );

        // Cookie session — the primary SPA flow. Doesn't generate Authorize-button UI in Swagger
        // (browsers manage cookies automatically) but advertises the scheme to OpenAPI consumers.
        options.AddSecurityDefinition(
            "Cookie",
            new()
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Cookie,
                Name = "shield.auth",
                Description = "Cookie issued by /api/auth/login. Used by the SPA.",
            }
        );

        // Every endpoint accepts Bearer-or-Cookie by default unless `[AllowAnonymous]`.
        options.AddSecurityRequirement(
            new()
            {
                {
                    new()
                    {
                        Reference = new()
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer",
                        },
                    },
                    Array.Empty<string>()
                },
            }
        );
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

// One-shot CPM rescan: enqueue source 77 (NoMercyLabs/shield) if the CPM-aware NuGet parser
// has never produced inventory for it. The marker AppSetting ensures this only fires once per
// installation so subsequent boots skip the enqueue. Skipped silently if source 77 doesn't
// exist in the DB (e.g. fresh installations that add it through the UI instead of seeding).
const string cpmRescanMarker = "shield.boot_rescan.cpm_v1";
using (IServiceScope cpmScope = app.Services.CreateScope())
{
    IAppSettingsService cpmSettings =
        cpmScope.ServiceProvider.GetRequiredService<IAppSettingsService>();
    bool alreadyRan = await cpmSettings.GetBoolAsync(cpmRescanMarker, fallback: false);
    if (!alreadyRan)
    {
        ShieldDbContext cpmDb = cpmScope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        bool sourceExists = await cpmDb.Sources.AnyAsync(source => source.Id == 77);
        if (sourceExists)
        {
            IPersistentScanQueue cpmQueue =
                cpmScope.ServiceProvider.GetRequiredService<IPersistentScanQueue>();
            await cpmQueue.EnqueueAsync(sourceId: 77);
            app.Logger.LogInformation(
                "CPM boot rescan: enqueued scan for source 77 (NoMercyLabs/shield)"
            );
        }
        await cpmSettings.SetBoolAsync(cpmRescanMarker, value: true, updatedBy: null);
    }
}

// Boot banner so operators see the chosen posture immediately in logs.
ProductionSafetyGate.LogPostureBanner(app.Logger, configuration, app.Environment);

// GHSA cadence sanity check — warn once at boot if running unauthenticated with a short
// cadence that will exhaust the 60 req/hr GitHub unauthenticated GraphQL quota quickly.
string? ghsaPat = configuration["Shield:Feeds:Ghsa:Pat"];
string? ghsaCadenceRaw = configuration["Shield:Feeds:Ghsa:Cadence"];
if (
    string.IsNullOrWhiteSpace(ghsaPat)
    && TimeSpan.TryParse(ghsaCadenceRaw, out TimeSpan ghsaCadence)
    && ghsaCadence < TimeSpan.FromMinutes(30)
)
{
    app.Logger.LogWarning(
        "GHSA cadence is {Cadence} but no API key is configured; "
            + "unauthenticated quota is 60 req/hr. "
            + "Either set Shield:Feeds:Ghsa:Pat to a GitHub personal access token "
            + "(read:packages scope is sufficient) or raise the cadence to 00:30:00 or longer.",
        ghsaCadence
    );
}

// Forwarded headers MUST run before HttpsRedirection so X-Forwarded-Proto is honoured by
// the redirect logic — otherwise we bounce-loop between the proxy and the app.
app.UseForwardedHeaders();

if (configuration.GetValue("Shield:Auth:RequireHttps", false))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

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
[
    app.Environment.WebRootPath ?? string.Empty,
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    Path.Combine(app.Environment.ContentRootPath, "wwwroot"),
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"),
];
string resolvedWebRoot =
    candidateWebRoots
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .FirstOrDefault(Directory.Exists)
    ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");

Microsoft.Extensions.FileProviders.PhysicalFileProvider spaFileProvider = new(resolvedWebRoot);

// Keep IWebHostEnvironment.WebRootFileProvider aligned with the resolved path so middleware
// that resolves IWebHostEnvironment from DI (e.g. CrawlerMetaMiddleware) reads index.html
// from the same wwwroot the static-file pipeline serves.
app.Environment.WebRootPath = resolvedWebRoot;
app.Environment.WebRootFileProvider = spaFileProvider;
app.Logger.LogInformation("SPA file provider rooted at {ResolvedWebRoot}", resolvedWebRoot);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = spaFileProvider });
app.UseStaticFiles(new StaticFileOptions { FileProvider = spaFileProvider });

app.UseRouting();

app.UseAuthentication();

// Short-circuit invalid `shld_` bearers to 401 before downstream schemes can satisfy the
// default policy — otherwise a revoked/expired api-token would silently fall back to the
// cookie/SingleUser/JWT principal that auth middleware happened to also authenticate.
app.UseApiTokenChallengeGate();
app.UseAuthorization();

// Rate limiter — runs after auth so 429s are attributable in audit, before controllers so
// the limiter sees the inbound path. The auth-burst policy is opted-in per route below via
// RequireRateLimiting on the controller routes that accept anonymous credentials.
app.UseRateLimiter();

// Session tracking — must run AFTER auth so the cookie + Identity principal are both
// resolved, and BEFORE the 2FA gate so the gate sees a coherent (cookie ↔ session row) pair.
// On a revoked row the middleware short-circuits with 401 + clears the cookie.
app.UseMiddleware<SessionTrackingMiddleware>();

// Impersonation — admin "view as user" override. MUST run AFTER session tracking (so the
// admin's real cookie is validated against a non-revoked session row first) and BEFORE
// every downstream gate that reads HttpContext.User (2FA enforcement, audit logger,
// IAccessResolver inside controllers). The middleware swaps the principal in-place; no
// short-circuit on missing/invalid cookie — those fall through with the real admin
// principal intact.
app.UseMiddleware<ImpersonationMiddleware>();

// 2FA enforcement — gates the API surface for non-2FA users when `auth.require_2fa=true`.
// Allows /api/auth/me + /api/auth/2fa/* through so the SPA can self-rescue.
app.UseMiddleware<TwoFactorEnforcementMiddleware>();

// Audit log middleware — runs AFTER auth so it can read the actor claims, and BEFORE
// MapControllers so the controller pipeline executes within its scope. Records 2xx writes
// for whitelisted admin actions (finding transitions, source/channel mutations, settings,
// OAuth connect/disconnect). See AuditMiddleware.Classify for the whitelist.
app.UseMiddleware<AuditMiddleware>();

// region OG (link-preview / Open Graph)
// Crawler-aware OG meta injection — must run after UseRouting/UseAuthentication so it
// participates in the standard pipeline, but BEFORE MapControllers/MapFallback so it can
// short-circuit bot GETs to non-/api/* routes with the enriched HTML. Regular browsers
// don't match the bot user-agent regex and fall through to the SPA fallback unchanged.
app.UseMiddleware<Shield.Api.Middleware.CrawlerMetaMiddleware>();

// endregion OG

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
