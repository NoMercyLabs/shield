using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Shield.Api.Services;
using Shield.Api.Services.BulkFix;
using Shield.Api.Workers.Queues;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class SourcesController : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 500;
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromHours(1);

    private readonly ShieldDbContext _db;
    private readonly IPersistentScanQueue _scanQueue;
    private readonly FeedsDbContext _feedsDb;
    private readonly IAnomalyDetector _anomalyDetector;
    private readonly IAccessResolver _access;
    private readonly IBulkApplyOrchestrator _bulkApply;

    public SourcesController(
        ShieldDbContext db,
        IPersistentScanQueue scanQueue,
        FeedsDbContext feedsDb,
        IAnomalyDetector anomalyDetector,
        IAccessResolver access,
        IBulkApplyOrchestrator bulkApply
    )
    {
        _db = db;
        _scanQueue = scanQueue;
        _feedsDb = feedsDb;
        _anomalyDetector = anomalyDetector;
        _access = access;
        _bulkApply = bulkApply;
    }

    [HttpGet]
    [RequireApiScope("sources:read")]
    public async Task<ActionResult<IReadOnlyList<SourceResponse>>> List(CancellationToken ct)
    {
        bool isAdmin = User.IsInRole(ShieldRoles.Admin);
        IQueryable<Source> query = _db.Sources;
        if (!isAdmin)
        {
            IReadOnlyList<int> visible = await _access.GetVisibleSourceIdsAsync(User, ct);
            if (visible.Count == 0)
                return Ok(Array.Empty<SourceResponse>());
            query = query.Where(source => visible.Contains(source.Id));
        }
        List<Source> sources = await query
            .OrderByDescending(source => source.UpdatedAt)
            .ToListAsync(ct);
        return Ok(sources.Select(SourceResponse.From).ToList());
    }

    [HttpGet("{id:int}")]
    [RequireApiScope("sources:read")]
    public async Task<ActionResult<SourceDetailResponse>> Get(int id, CancellationToken ct)
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();

        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();

        InventorySnapshot? snapshot = await _db
            .InventorySnapshots.Where(item => item.SourceId == id)
            .OrderByDescending(item => item.TakenAt)
            .FirstOrDefaultAsync(ct);

        SnapshotSummary? summary = null;
        if (snapshot is not null)
        {
            Dictionary<Ecosystem, int> ecosystems = await _db
                .InventoryItems.Where(item => item.SnapshotId == snapshot.Id)
                .GroupBy(item => item.Ecosystem)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, ct);

            summary = new(
                snapshot.Id,
                snapshot.TakenAt,
                snapshot.ContentsSha,
                snapshot.ItemCount,
                ecosystems
            );
        }

        return Ok(new SourceDetailResponse(SourceResponse.From(source), summary));
    }

    [HttpPost]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<SourceResponse>> Create(
        [FromBody] CreateSourceRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Name is required.");

        if (
            !TryNormaliseConfig(
                request.Type,
                request.ConfigJson,
                out string configJson,
                out string? configError
            )
        )
            return ValidationProblem(configError ?? "Invalid configJson.");

        DateTime now = DateTime.UtcNow;
        Source source = new()
        {
            Type = request.Type,
            Name = request.Name,
            ConfigJson = configJson,
            ScanInterval = request.ScanInterval ?? DefaultScanInterval,
            Enabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Sources.Add(source);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = source.Id }, SourceResponse.From(source));
    }

    // Bulk-create LocalFolder sources from a list of absolute paths. Admin only.
    // Dedupes against existing LocalFolder sources that already reference the same path.
    [HttpPost("bulk-local-folders")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<BulkLocalFoldersResponse>> BulkLocalFolders(
        [FromBody] BulkLocalFoldersRequest request,
        CancellationToken ct
    )
    {
        if (request.Paths is null || request.Paths.Count == 0)
            return ValidationProblem("paths is required.");

        if (!TimeSpan.TryParse(request.DefaultScanInterval, out TimeSpan scanInterval))
            scanInterval = DefaultScanInterval;

        List<Source> existing = await _db
            .Sources.Where(source => source.Type == SourceType.LocalFolder)
            .ToListAsync(ct);

        HashSet<string> existingPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Source source in existing)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(source.ConfigJson);
                if (
                    doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("path", out JsonElement pathProp)
                    && pathProp.ValueKind == JsonValueKind.String
                )
                {
                    string? path = pathProp.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        existingPaths.Add(Path.GetFullPath(path));
                }
            }
            catch (JsonException)
            {
                // ignore malformed legacy rows.
            }
        }

        DateTime now = DateTime.UtcNow;
        int created = 0;
        int skipped = 0;
        List<Source> newSources = [];

        foreach (string rawPath in request.Paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                skipped++;
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                skipped++;
                continue;
            }

            if (!existingPaths.Add(fullPath))
            {
                skipped++;
                continue;
            }

            string configJson = JsonSerializer.Serialize(new { path = fullPath });
            Source source = new()
            {
                Type = SourceType.LocalFolder,
                Name =
                    $"folder:{Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}",
                ConfigJson = configJson,
                ScanInterval = scanInterval,
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.Sources.Add(source);
            newSources.Add(source);
            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(ct);
            await EnqueueBulkAsync(newSources, ct);
        }

        return Ok(
            new BulkLocalFoldersResponse(
                Created: created,
                SkippedExisting: skipped,
                Sources: newSources.Select(SourceResponse.From).ToList()
            )
        );
    }

    [HttpPut("{id:int}")]
    [NoApiToken]
    public async Task<ActionResult<SourceResponse>> Update(
        int id,
        [FromBody] UpdateSourceRequest request,
        CancellationToken ct
    )
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();
        if (!await _access.CanTriageAsync(User, id, ct))
            return Forbid();

        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return ValidationProblem("Name is required.");

        if (
            !TryNormaliseConfig(
                source.Type,
                request.ConfigJson,
                out string configJson,
                out string? configError
            )
        )
            return ValidationProblem(configError ?? "Invalid configJson.");

        source.Name = request.Name;
        source.ConfigJson = configJson;
        source.ScanInterval = request.ScanInterval ?? source.ScanInterval;
        source.Enabled = request.Enabled;
        if (request.MinPackageAgeHours.HasValue)
        {
            // Clamp at [0, 720] (30 days). Zero disables the warning; anything above 30 days is
            // almost always a misconfiguration that would silence legitimate security fixes.
            source.MinPackageAgeHours = Math.Clamp(request.MinPackageAgeHours.Value, 0, 720);
        }
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(SourceResponse.From(source));
    }

    [HttpDelete("{id:int}")]
    [NoApiToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();
        if (!await _access.CanTriageAsync(User, id, ct))
            return Forbid();

        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();
        // Cascade — manual cleanup for tables EF doesn't auto-cascade. PackageUpdates lacks an
        // FK to Source (kept loose so a re-added repo by name doesn't accidentally rehydrate
        // history), so wipe by SourceId before removing the source row.
        List<Core.Domain.PackageUpdate> orphans = await _db
            .PackageUpdates.Where(update => update.SourceId == id)
            .ToListAsync(ct);
        if (orphans.Count > 0)
            _db.PackageUpdates.RemoveRange(orphans);
        _db.Sources.Remove(source);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Creates a sibling GithubRepo source from a LocalFolder's detected origin.
    // Admin only — same gate as Settings + OAuth.
    [HttpPost("{id:int}/promote-to-github")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<SourceResponse>> PromoteToGithub(int id, CancellationToken ct)
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();
        if (source.Type != SourceType.LocalFolder)
            return BadRequest(new { error = "Promote is only valid for LocalFolder sources." });

        DetectedRemoteDto? detected = SourceResponse.From(source).DetectedRemote;
        if (detected is null)
            return BadRequest(new { error = "No detected remote on this source." });
        if (!string.Equals(detected.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return BadRequest(
                new
                {
                    error = $"Detected host '{detected.Host}' is not promotable; only github.com is supported.",
                }
            );

        DateTime now = DateTime.UtcNow;
        string siblingConfig = JsonSerializer.Serialize(
            new
            {
                owner = detected.Owner,
                repo = detected.Repo,
                branch = string.IsNullOrWhiteSpace(detected.Branch) ? null : detected.Branch,
            }
        );

        Source sibling = new()
        {
            Type = SourceType.GithubRepo,
            Name = $"{source.Name} (GitHub)",
            ConfigJson = siblingConfig,
            ScanInterval = source.ScanInterval,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Sources.Add(sibling);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = sibling.Id }, SourceResponse.From(sibling));
    }

    // Pick-from-GitHub: take a list of {owner, name, branch?} and materialise GithubRepo sources
    // in one transaction. Idempotent — a selection whose `owner/name` already exists is skipped.
    // Token is read server-side from the OAuth IntegrationToken; never stored in ConfigJson.
    [HttpPost("bulk-from-github")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<BulkFromGithubResponse>> BulkFromGithub(
        [FromBody] BulkFromGithubRequest request,
        CancellationToken ct
    )
    {
        if (request is null || request.Selections is null || request.Selections.Count == 0)
            return ValidationProblem("At least one selection is required.");

        TimeSpan scanInterval = request.DefaultScanInterval ?? TimeSpan.FromHours(6);
        if (scanInterval <= TimeSpan.Zero)
            scanInterval = TimeSpan.FromHours(6);

        // Pull existing GithubRepo names once so we can skip duplicates inside the loop without N round-trips.
        HashSet<string> existingNames = (
            await _db
                .Sources.Where(source => source.Type == SourceType.GithubRepo)
                .Select(source => source.Name)
                .ToListAsync(ct)
        ).ToHashSet(StringComparer.OrdinalIgnoreCase);

        DateTime now = DateTime.UtcNow;
        List<Source> toInsert = [];
        int skipped = 0;

        foreach (BulkSelection selection in request.Selections)
        {
            if (
                selection is null
                || string.IsNullOrWhiteSpace(selection.Owner)
                || string.IsNullOrWhiteSpace(selection.Name)
            )
            {
                skipped++;
                continue;
            }
            string name = $"{selection.Owner}/{selection.Name}";
            if (!existingNames.Add(name))
            {
                skipped++;
                continue;
            }
            string configJson = JsonSerializer.Serialize(
                new
                {
                    owner = selection.Owner,
                    repo = selection.Name,
                    branch = string.IsNullOrWhiteSpace(selection.Branch) ? null : selection.Branch,
                }
            );
            toInsert.Add(
                new()
                {
                    Type = SourceType.GithubRepo,
                    Name = name,
                    ConfigJson = configJson,
                    ScanInterval = scanInterval,
                    Enabled = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                }
            );
        }

        if (toInsert.Count > 0)
        {
            _db.Sources.AddRange(toInsert);
            await _db.SaveChangesAsync(ct);
            await EnqueueBulkAsync(toInsert, ct);
        }

        List<SourceResponse> created = toInsert.Select(SourceResponse.From).ToList();
        return Ok(new BulkFromGithubResponse(created.Count, skipped, created));
    }

    [HttpPost("{id:int}/scan-now")]
    [NoApiToken]
    public async Task<ActionResult<ScanQueuedResponse>> ScanNow(int id, CancellationToken ct)
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();
        if (!await _access.CanTriageAsync(User, id, ct))
            return Forbid();

        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();
        DateTime queuedAt = DateTime.UtcNow;
        await _scanQueue.EnqueueAsync(id, ct);
        // EstimatedCompletion is nullable for now — worker is single-threaded, no reliable ETA.
        return Accepted(
            new ScanQueuedResponse(Accepted: true, QueuedAt: queuedAt, EstimatedCompletion: null)
        );
    }

    [HttpPost("{id:int}/apply-all-fixes")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    [EnableRateLimiting("bulk-apply")]
    public async Task<ActionResult<BulkApplyResponse>> ApplyAllFixes(
        int id,
        [FromBody] BulkApplyRequest request,
        CancellationToken ct
    )
    {
        BulkApplyDispatchResult result = await _bulkApply.ApplyAsync(id, request, ct);
        return result.Outcome switch
        {
            BulkApplyOutcome.SourceNotFound => NotFound(),
            BulkApplyOutcome.UnsupportedType => BadRequest(
                new { error = result.ErrorCode, message = result.ErrorMessage }
            ),
            BulkApplyOutcome.ProductionConfirmationRequired => StatusCode(
                StatusCodes.Status409Conflict,
                new { error = result.ErrorCode, message = result.ErrorMessage }
            ),
            BulkApplyOutcome.Cooldown => StatusCode(
                StatusCodes.Status429TooManyRequests,
                new
                {
                    error = result.ErrorCode,
                    retryAfter = result.RetryAfter?.ToString("o"),
                    message = result.ErrorMessage,
                }
            ),
            _ => Ok(result.Response),
        };
    }

    [HttpPatch("{id:int}/auto-fix-mode")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<SourceResponse>> SetAutoFixMode(
        int id,
        [FromBody] SetAutoFixModeRequest request,
        CancellationToken ct
    )
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();

        source.AutoFixMode = request.AutoFixMode;
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(SourceResponse.From(source));
    }

    [HttpPatch("{id:int}/is-production")]
    [Authorize(Policy = ShieldPolicies.Admin)]
    [NoApiToken]
    public async Task<ActionResult<SourceResponse>> SetIsProduction(
        int id,
        [FromBody] SetIsProductionRequest request,
        CancellationToken ct
    )
    {
        Source? source = await _db.Sources.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (source is null)
            return NotFound();

        source.IsProduction = request.IsProduction;
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(SourceResponse.From(source));
    }

    // Enqueue scans for the freshly-created sources from a bulk-add response. Shared between
    // bulk-from-github and bulk-local-folders so both call sites get identical semantics.
    private async Task EnqueueBulkAsync(IEnumerable<Source> sources, CancellationToken ct)
    {
        List<int> ids = sources.Select(source => source.Id).ToList();
        if (ids.Count == 0)
            return;
        await _scanQueue.EnqueueManyAsync(ids, ct);
    }

    [HttpGet("{id:int}/snapshots")]
    [RequireApiScope("sources:read")]
    public async Task<ActionResult<IReadOnlyList<SnapshotListItem>>> Snapshots(
        int id,
        CancellationToken ct
    )
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();

        bool exists = await _db.Sources.AnyAsync(source => source.Id == id, ct);
        if (!exists)
            return NotFound();

        List<InventorySnapshot> rawSnapshots = await _db
            .InventorySnapshots.Where(snapshot => snapshot.SourceId == id)
            .OrderByDescending(snapshot => snapshot.TakenAt)
            .ToListAsync(ct);

        // Pair each snapshot with the one immediately older so the UI can render
        // "Compare with previous" links without a second round-trip.
        List<SnapshotListItem> snapshots = rawSnapshots
            .Select(
                (snapshot, index) =>
                    new SnapshotListItem(
                        snapshot.Id,
                        snapshot.TakenAt,
                        snapshot.ContentsSha,
                        snapshot.ItemCount,
                        index + 1 < rawSnapshots.Count ? rawSnapshots[index + 1].Id : null
                    )
            )
            .ToList();

        return Ok(snapshots);
    }

    // Snapshot-to-snapshot diff with supply-chain anomaly flags on added items.
    // Path order /{olderId}/diff/{newerId} keeps the older snapshot adjacent to the
    // resource it's already namespaced under and avoids a {guid}/{guid} ambiguity.
    [HttpGet("{id:int}/snapshots/{olderId:guid}/diff/{newerId:guid}")]
    [RequireApiScope("sources:read")]
    public async Task<ActionResult<SnapshotDiffResponse>> Diff(
        int id,
        Guid olderId,
        Guid newerId,
        CancellationToken ct
    )
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();
        if (olderId == newerId)
            return ValidationProblem("olderId and newerId must differ.");

        InventorySnapshot? older = await _db
            .InventorySnapshots.AsNoTracking()
            .FirstOrDefaultAsync(snapshot => snapshot.Id == olderId && snapshot.SourceId == id, ct);
        InventorySnapshot? newer = await _db
            .InventorySnapshots.AsNoTracking()
            .FirstOrDefaultAsync(snapshot => snapshot.Id == newerId && snapshot.SourceId == id, ct);
        if (older is null || newer is null)
            return NotFound();

        List<InventoryItem> olderItems = await _db
            .InventoryItems.AsNoTracking()
            .Where(item => item.SnapshotId == olderId)
            .ToListAsync(ct);
        List<InventoryItem> newerItems = await _db
            .InventoryItems.AsNoTracking()
            .Where(item => item.SnapshotId == newerId)
            .ToListAsync(ct);

        Dictionary<(Ecosystem, string), InventoryItem> olderIndex = olderItems
            .GroupBy(item => (item.Ecosystem, NormalizeDiffName(item.Name)))
            .ToDictionary(group => group.Key, group => group.First());
        Dictionary<(Ecosystem, string), InventoryItem> newerIndex = newerItems
            .GroupBy(item => (item.Ecosystem, NormalizeDiffName(item.Name)))
            .ToDictionary(group => group.Key, group => group.First());

        List<InventoryItem> addedRaw = newerItems
            .Where(item => !olderIndex.ContainsKey((item.Ecosystem, NormalizeDiffName(item.Name))))
            .ToList();
        List<InventoryItem> removedRaw = olderItems
            .Where(item => !newerIndex.ContainsKey((item.Ecosystem, NormalizeDiffName(item.Name))))
            .ToList();

        List<InventoryDiffChange> versionChanged = [];
        foreach (InventoryItem newerItem in newerItems)
        {
            if (
                olderIndex.TryGetValue(
                    (newerItem.Ecosystem, NormalizeDiffName(newerItem.Name)),
                    out InventoryItem? olderItem
                ) && !string.Equals(olderItem.Version, newerItem.Version, StringComparison.Ordinal)
            )
            {
                versionChanged.Add(
                    new(
                        newerItem.Ecosystem,
                        newerItem.Name,
                        olderItem.Version,
                        newerItem.Version,
                        newerItem.IsDirect
                    )
                );
            }
        }

        DateTime nowUtc = DateTime.UtcNow;
        List<InventoryDiffEntry> added = new(addedRaw.Count);
        foreach (InventoryItem item in addedRaw)
        {
            PackageMeta? current = await _feedsDb
                .PackageMetas.AsNoTracking()
                .FirstOrDefaultAsync(
                    meta =>
                        meta.Ecosystem == item.Ecosystem
                        && meta.Name == item.Name
                        && meta.Version == item.Version,
                    ct
                );
            PackageMeta? prior = await _feedsDb
                .PackageMetas.AsNoTracking()
                .Where(meta =>
                    meta.Ecosystem == item.Ecosystem
                    && meta.Name == item.Name
                    && meta.Version != item.Version
                )
                .OrderByDescending(meta => meta.PublishedAt ?? meta.FetchedAt)
                .FirstOrDefaultAsync(ct);

            AnomalyFlags flags = _anomalyDetector.Evaluate(
                item.Ecosystem,
                item.Name,
                item.Version,
                current,
                prior,
                nowUtc
            );

            added.Add(
                new(item.Ecosystem, item.Name, item.Version, item.IsDirect, item.ParentChain, flags)
            );
        }

        List<InventoryDiffEntry> removed = removedRaw
            .Select(item => new InventoryDiffEntry(
                item.Ecosystem,
                item.Name,
                item.Version,
                item.IsDirect,
                item.ParentChain,
                AnomalyFlags.None
            ))
            .ToList();

        SnapshotSummary olderSummary = await BuildSnapshotSummaryAsync(older, ct);
        SnapshotSummary newerSummary = await BuildSnapshotSummaryAsync(newer, ct);

        return Ok(
            new SnapshotDiffResponse(olderSummary, newerSummary, added, removed, versionChanged)
        );
    }

    private async Task<SnapshotSummary> BuildSnapshotSummaryAsync(
        InventorySnapshot snapshot,
        CancellationToken ct
    )
    {
        Dictionary<Ecosystem, int> ecosystems = await _db
            .InventoryItems.AsNoTracking()
            .Where(item => item.SnapshotId == snapshot.Id)
            .GroupBy(item => item.Ecosystem)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, ct);

        return new(
            snapshot.Id,
            snapshot.TakenAt,
            snapshot.ContentsSha,
            snapshot.ItemCount,
            ecosystems
        );
    }

    private static string NormalizeDiffName(string name) => name.Trim().ToLowerInvariant();

    [HttpGet("{id:int}/snapshots/{snapshotId:guid}/items")]
    [RequireApiScope("sources:read")]
    public async Task<ActionResult<PagedResponse<InventoryItemResponse>>> SnapshotItems(
        int id,
        Guid snapshotId,
        [FromQuery] Ecosystem? ecosystem,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default
    )
    {
        if (!await _access.CanReadAsync(User, id, ct))
            return NotFound();
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        InventorySnapshot? snapshot = await _db.InventorySnapshots.FirstOrDefaultAsync(
            entry => entry.Id == snapshotId && entry.SourceId == id,
            ct
        );
        if (snapshot is null)
            return NotFound();

        IQueryable<InventoryItem> query = _db.InventoryItems.Where(item =>
            item.SnapshotId == snapshotId
        );

        if (ecosystem.HasValue)
            query = query.Where(item => item.Ecosystem == ecosystem.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string needle = search.Trim();
            query = query.Where(item => EF.Functions.Like(item.Name, $"%{needle}%"));
        }

        int total = await query.CountAsync(ct);
        List<InventoryItemResponse> items = await query
            .OrderBy(item => item.Ecosystem)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new InventoryItemResponse(
                item.Id,
                item.Ecosystem,
                item.Name,
                item.Version,
                item.IsDirect,
                item.ParentChain
            ))
            .ToListAsync(ct);

        return Ok(new PagedResponse<InventoryItemResponse>(items, total, page, pageSize));
    }

    // Accept ConfigJson as either an object or a JSON-encoded string. Object → serialise.
    // Validates required fields per source type (path for LocalFolder, owner+repo for GithubRepo).
    private static bool TryNormaliseConfig(
        SourceType type,
        JsonElement element,
        out string configJson,
        out string? error
    )
    {
        configJson = "{}";
        error = null;

        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            error = "configJson is required.";
            return false;
        }

        // String input — accept verbatim once we confirm it parses as object.
        if (element.ValueKind == JsonValueKind.String)
        {
            string raw = element.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "configJson is required.";
                return false;
            }
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(raw);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = "configJson must encode a JSON object.";
                    return false;
                }
                return ValidateConfig(type, parsed.RootElement, raw, out configJson, out error);
            }
            catch (JsonException ex)
            {
                error = $"configJson is not valid JSON: {ex.Message}";
                return false;
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "configJson must be a JSON object or a JSON-encoded string.";
            return false;
        }

        string serialised = element.GetRawText();
        return ValidateConfig(type, element, serialised, out configJson, out error);
    }

    private static bool ValidateConfig(
        SourceType type,
        JsonElement root,
        string serialised,
        out string configJson,
        out string? error
    )
    {
        configJson = serialised;
        error = null;

        switch (type)
        {
            case SourceType.LocalFolder:
                if (
                    !TryGetString(root, "path", out string? path) || string.IsNullOrWhiteSpace(path)
                )
                {
                    error = "LocalFolder configJson requires a non-empty 'path'.";
                    return false;
                }
                break;
            case SourceType.GithubRepo:
                if (
                    !TryGetString(root, "owner", out string? owner)
                    || string.IsNullOrWhiteSpace(owner)
                )
                {
                    error = "GithubRepo configJson requires a non-empty 'owner'.";
                    return false;
                }
                if (
                    !TryGetString(root, "repo", out string? repo) || string.IsNullOrWhiteSpace(repo)
                )
                {
                    error = "GithubRepo configJson requires a non-empty 'repo'.";
                    return false;
                }
                break;
        }
        return true;
    }

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property))
            return false;
        if (property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return true;
    }
}
