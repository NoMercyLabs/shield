using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Data;
using Shield.Data.Identity;
using WebPush;
// Two PushSubscription types collide here — Shield's domain row and WebPush's wire-level
// request DTO. Use aliases (instead of importing both namespaces) so every reference is
// unambiguous and the reader sees the intent at a glance.
using ShieldPushSubscription = Shield.Core.Domain.PushSubscription;
using WebPushSubscription = WebPush.PushSubscription;

namespace Shield.Api.Services.Notifications;

// Singleton. Resolves IAppSettingsService (singleton) to read/write the VAPID identity,
// opens a fresh scope per dispatch for ShieldDbContext access (PushSubscription rows).
// The WebPush NuGet client itself is stateless — one instance per call is cheap and
// avoids holding a stale VAPID identity across rotations.
public sealed class WebPushSender : IWebPushSender
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<WebPushSender> _logger;
    private readonly SemaphoreSlim _vapidLock = new(1, 1);
    private VapidDetails? _vapid;

    public WebPushSender(
        IServiceScopeFactory scopeFactory,
        IAppSettingsService settings,
        ILogger<WebPushSender> logger
    )
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<string> EnsureVapidPublicKeyAsync(CancellationToken ct = default)
    {
        VapidDetails vapid = await EnsureVapidAsync(ct);
        return vapid.PublicKey;
    }

    public async Task DispatchAsync(
        PushPayload payload,
        Guid? userId,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();

        List<ShieldPushSubscription> subscriptions;
        if (userId is null)
        {
            // Broadcast — fan out to every admin's subscriptions. Mirrors the in-app
            // notification contract: UserId=null is admin-wide.
            UserManager<ShieldUser> userManager = scope.ServiceProvider.GetRequiredService<
                UserManager<ShieldUser>
            >();
            IList<ShieldUser> admins = await userManager.GetUsersInRoleAsync(ShieldRoles.Admin);
            Guid[] adminIds = admins.Select(user => user.Id).ToArray();
            subscriptions = await db
                .PushSubscriptions.Where(subscription => adminIds.Contains(subscription.UserId))
                .ToListAsync(ct);
        }
        else
        {
            subscriptions = await db
                .PushSubscriptions.Where(subscription => subscription.UserId == userId.Value)
                .ToListAsync(ct);
        }

        if (subscriptions.Count == 0)
            return;

        VapidDetails vapid = await EnsureVapidAsync(ct);
        string body = JsonSerializer.Serialize(
            new
            {
                payload.Title,
                payload.Body,
                payload.Severity,
                payload.Url,
                payload.Tag,
            },
            PayloadSerializerOptions
        );

        WebPushClient client = new();
        DateTime now = DateTime.UtcNow;
        List<Guid> deadIds = [];
        bool anyDelivered = false;

        foreach (ShieldPushSubscription subscription in subscriptions)
        {
            WebPushSubscription target = new(
                subscription.Endpoint,
                subscription.P256dh,
                subscription.Auth
            );
            try
            {
                await client.SendNotificationAsync(target, body, vapid, ct);
                subscription.LastDeliveredAt = now;
                anyDelivered = true;
            }
            catch (WebPushException ex)
                when (ex.StatusCode == System.Net.HttpStatusCode.Gone
                    || ex.StatusCode == System.Net.HttpStatusCode.NotFound
                )
            {
                // 410 Gone / 404 = the upstream push service forgot the endpoint (user
                // revoked the grant or cleared site data). Hard-delete so we stop spending
                // on every broadcast.
                deadIds.Add(subscription.Id);
                _logger.LogInformation(
                    "Dropping dead push subscription {SubscriptionId} (status {Status})",
                    subscription.Id,
                    ex.StatusCode
                );
            }
            catch (WebPushException ex)
            {
                // Transient — one retry, then drop. The next notification will land fine
                // if the upstream recovers; no point queueing.
                _logger.LogWarning(
                    ex,
                    "Web push delivery failed for {SubscriptionId} (status {Status}); retrying once",
                    subscription.Id,
                    ex.StatusCode
                );
                try
                {
                    await client.SendNotificationAsync(target, body, vapid, ct);
                    subscription.LastDeliveredAt = now;
                    anyDelivered = true;
                }
                catch (Exception retryEx)
                {
                    _logger.LogWarning(
                        retryEx,
                        "Web push retry failed for {SubscriptionId}; dropping this delivery",
                        subscription.Id
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unexpected error sending push to {SubscriptionId}",
                    subscription.Id
                );
            }
        }

        if (deadIds.Count > 0)
        {
            await db
                .PushSubscriptions.Where(subscription => deadIds.Contains(subscription.Id))
                .ExecuteDeleteAsync(ct);
        }

        if (anyDelivered)
            await db.SaveChangesAsync(ct);
    }

    public async Task DispatchToSubscriptionAsync(
        PushPayload payload,
        Guid subscriptionId,
        CancellationToken ct = default
    )
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ShieldDbContext db = scope.ServiceProvider.GetRequiredService<ShieldDbContext>();
        ShieldPushSubscription? subscription = await db.PushSubscriptions.FirstOrDefaultAsync(
            row => row.Id == subscriptionId,
            ct
        );
        if (subscription is null)
            return;

        VapidDetails vapid = await EnsureVapidAsync(ct);
        string body = JsonSerializer.Serialize(
            new
            {
                payload.Title,
                payload.Body,
                payload.Severity,
                payload.Url,
                payload.Tag,
            },
            PayloadSerializerOptions
        );
        WebPushClient client = new();
        WebPushSubscription target = new(
            subscription.Endpoint,
            subscription.P256dh,
            subscription.Auth
        );
        try
        {
            await client.SendNotificationAsync(target, body, vapid, ct);
            subscription.LastDeliveredAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (WebPushException ex)
            when (ex.StatusCode == System.Net.HttpStatusCode.Gone
                || ex.StatusCode == System.Net.HttpStatusCode.NotFound
            )
        {
            await db
                .PushSubscriptions.Where(row => row.Id == subscriptionId)
                .ExecuteDeleteAsync(ct);
            throw;
        }
    }

    private async Task<VapidDetails> EnsureVapidAsync(CancellationToken ct)
    {
        if (_vapid is not null)
            return _vapid;

        await _vapidLock.WaitAsync(ct);
        try
        {
            if (_vapid is not null)
                return _vapid;

            string? publicKey = await _settings.GetStringAsync(
                AppSettingKeys.PushVapidPublicKey,
                ct
            );
            string? privateKey = await _settings.GetStringAsync(
                AppSettingKeys.PushVapidPrivateKey,
                ct
            );
            string? subject = await _settings.GetStringAsync(AppSettingKeys.PushVapidSubject, ct);

            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
            {
                VapidDetails generated = VapidHelper.GenerateVapidKeys();
                publicKey = generated.PublicKey;
                privateKey = generated.PrivateKey;
                await _settings.SetStringAsync(
                    AppSettingKeys.PushVapidPublicKey,
                    publicKey,
                    updatedBy: null,
                    ct
                );
                await _settings.SetStringAsync(
                    AppSettingKeys.PushVapidPrivateKey,
                    privateKey,
                    updatedBy: null,
                    ct
                );
                _logger.LogInformation("Generated fresh VAPID keypair for web push");
            }

            // Subject must be either a mailto: or https: URL. Operators may override via
            // settings; otherwise we fall back to a generic mailto so the payload is valid
            // even on a fresh install.
            if (string.IsNullOrEmpty(subject))
                subject = "mailto:admin@shield.local";

            _vapid = new(subject, publicKey, privateKey);
            return _vapid;
        }
        finally
        {
            _vapidLock.Release();
        }
    }
}
