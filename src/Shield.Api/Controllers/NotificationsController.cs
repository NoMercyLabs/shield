using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private readonly ShieldDbContext _db;
    private readonly UserManager<ShieldUser> _userManager;

    public NotificationsController(ShieldDbContext db, UserManager<ShieldUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationsPage>> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken ct = default
    )
    {
        if (limit < 1)
            limit = DefaultLimit;
        if (limit > MaxLimit)
            limit = MaxLimit;

        Guid userId = await ResolveUserIdAsync();

        // Broadcast rows (UserId IS NULL) reach every logged-in user. Per-user rows match
        // on UserId. Archived rows are excluded from the list view but still counted nowhere.
        IQueryable<Notification> baseQuery = _db.Notifications.Where(notification =>
            notification.ArchivedAt == null
            && (notification.UserId == null || notification.UserId == userId)
        );

        IQueryable<Notification> query = baseQuery;
        if (unreadOnly)
            query = query.Where(notification => notification.ReadAt == null);

        List<Notification> rows = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        int unreadCount = await baseQuery
            .Where(notification => notification.ReadAt == null)
            .CountAsync(ct);

        IReadOnlyList<NotificationResponse> items = rows.Select(NotificationResponse.From).ToList();
        return Ok(new NotificationsPage(items, unreadCount));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid id, CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        Notification? notification = await FindVisibleAsync(id, userId, ct);
        if (notification is null)
            return NotFound();
        if (notification.ReadAt is null)
        {
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Ok(NotificationResponse.From(notification));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<NotificationResponse>> Archive(Guid id, CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        Notification? notification = await FindVisibleAsync(id, userId, ct);
        if (notification is null)
            return NotFound();
        DateTime now = DateTime.UtcNow;
        notification.ArchivedAt = now;
        if (notification.ReadAt is null)
            notification.ReadAt = now;
        await _db.SaveChangesAsync(ct);
        return Ok(NotificationResponse.From(notification));
    }

    [HttpPost("mark-all-read")]
    public async Task<ActionResult<MarkAllReadResponse>> MarkAllRead(CancellationToken ct)
    {
        Guid userId = await ResolveUserIdAsync();
        DateTime now = DateTime.UtcNow;
        List<Notification> unread = await _db
            .Notifications.Where(notification =>
                notification.ReadAt == null
                && notification.ArchivedAt == null
                && (notification.UserId == null || notification.UserId == userId)
            )
            .ToListAsync(ct);
        foreach (Notification notification in unread)
            notification.ReadAt = now;
        if (unread.Count > 0)
            await _db.SaveChangesAsync(ct);
        return Ok(new MarkAllReadResponse(unread.Count));
    }

    private Task<Notification?> FindVisibleAsync(Guid id, Guid userId, CancellationToken ct) =>
        _db.Notifications.FirstOrDefaultAsync(
            notification =>
                notification.Id == id
                && (notification.UserId == null || notification.UserId == userId),
            ct
        );

    private async Task<Guid> ResolveUserIdAsync()
    {
        string? rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(rawId, out Guid parsed))
            return parsed;
        ShieldUser? user = await _userManager.GetUserAsync(User);
        return user?.Id ?? Guid.Empty;
    }
}
