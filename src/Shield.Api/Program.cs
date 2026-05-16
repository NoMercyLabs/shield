using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Shield.Alerter.Extensions;
using Shield.Api.Auth;
using Shield.Api.Persistence;
using Shield.Api.Workers;
using Shield.Channels.Extensions;
using Shield.Channels.Inbox;
using Shield.Core.Options;
using Shield.Data;
using Shield.Data.Extensions;
using Shield.Data.Identity;
using Shield.Feeds.Ghsa.Extensions;
using Shield.Feeds.NpmRegistry.Extensions;
using Shield.Feeds.Osv.Extensions;
using Shield.Matcher.Extensions;
using Shield.Parsers.Composer.Extensions;
using Shield.Parsers.Gradle.Extensions;
using Shield.Parsers.Npm.Extensions;
using Shield.Parsers.Nuget.Extensions;
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

// Identity — cookies for the SPA, JWT bearer for API clients.
builder
    .Services.AddIdentity<ShieldUser, ShieldRole>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.SignIn.RequireConfirmedAccount = false;
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

builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
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
    );

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "shield.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
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

builder.Services.AddAuthorization();

builder.Services.AddControllers();

bool enableOpenApi =
    builder.Environment.IsDevelopment() || configuration.GetValue("Shield:OpenApi:Enabled", false);
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

if (enableOpenApi)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

bool singleUser = configuration.GetValue("Shield:SingleUser", false);
if (singleUser)
{
    app.UseMiddleware<SingleUserMiddleware>();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
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
    string indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (File.Exists(indexPath))
        return context.Response.SendFileAsync(indexPath);
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

app.Run();

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
