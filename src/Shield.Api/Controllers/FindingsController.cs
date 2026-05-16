using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shield.Api.Auth;
using Shield.Api.Contracts;
using Shield.Api.Services;
using Shield.Core.Domain;
using Shield.Data;

namespace Shield.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class FindingsController : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;
    private const int MaxBulkSize = 500;

    private readonly ShieldDbContext _shieldDb;
    private readonly FeedsDbContext _feedsDb;
    private readonly IFixSuggester _fixSuggester;
    private readonly IFixApplier _fixApplier;
    private readonly IAccessResolver _access;

    public FindingsController(
        ShieldDbContext shieldDb,
        FeedsDbContext feedsDb,
        IFixSuggester fixSuggester,
        IFixApplier fixApplier,
        IAccessResolver access
    )
    {
        _shieldDb = shieldDb;
        _feedsDb = feedsDb;
        _fixSuggester = fixSuggester;
        _fixApplier = fixApplier;
        _access = access;
    }

    [HttpGet]
    public async Task<ActionResult<FindingsPage>> List(
        [FromQuery] List<Severity>? severity,
        [FromQuery] List<int>? sourceId,
        [FromQuery] List<Ecosystem>? ecosystem,
        [FromQuery] List<FindingState>? state,
        [FromQuery] List<string>? packageName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize)
            pageSize = MaxPageSize;

        IQueryable<Finding> query = _shieldDb.Findings.AsQueryable();

        // Per-source ACL — non-Admins only see findings on sources they were granted.
        if (!User.IsInRole(ShieldRoles.Admin))
        {
            IReadOnlyList<int> visible = await _access.GetVisibleSourceIdsAsync(User, ct);
            if (visible.Count == 0)
                return Ok(new FindingsPage(Array.Empty<FindingResponse>(), 0, page, pageSize));
            query = query.Where(finding => visible.Contains(finding.SourceId));
        }

        if (severity is { Count: > 0 })
            query = query.Where(finding => severity.Contains(finding.Severity));
        if (sourceId is { Count: > 0 })
            query = query.Where(finding => sourceId.Contains(finding.SourceId));
        if (state is { Count: > 0 })
            query = query.Where(finding => state.Contains(finding.State));

        if (ecosystem is { Count: > 0 })
        {
            IQueryable<int> itemIds = _shieldDb
                .InventoryItems.Where(item => ecosystem.Contains(item.Ecosystem))
                .Select(item => item.Id);
            query = query.Where(finding => itemIds.Contains(finding.InventoryItemId));
        }

        if (packageName is { Count: > 0 })
        {
            List<string> names = packageName
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToList();
            if (names.Count > 0)
            {
                IQueryable<int> itemIds = _shieldDb
                    .InventoryItems.Where(item => names.Contains(item.Name))
                    .Select(item => item.Id);
                query = query.Where(finding => itemIds.Contains(finding.InventoryItemId));
            }
        }

        int total = await query.CountAsync(ct);
        List<Finding> items = await query
            .OrderByDescending(finding => finding.LastSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        List<FindingResponse> enriched = await EnrichAsync(items, ct);
        return Ok(new FindingsPage(enriched, total, page, pageSize));
    }

    private async Task<List<FindingResponse>> EnrichAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        if (findings.Count == 0)
            return new();

        HashSet<int> sourceIds = findings.Select(finding => finding.SourceId).ToHashSet();
        HashSet<int> itemIds = findings.Select(finding => finding.InventoryItemId).ToHashSet();
        HashSet<Guid> advisoryIds = findings.Select(finding => finding.AdvisoryRefId).ToHashSet();

        Dictionary<int, string> sourceNames = await _shieldDb
            .Sources.Where(source => sourceIds.Contains(source.Id))
            .ToDictionaryAsync(source => source.Id, source => source.Name, ct);

        Dictionary<int, InventoryItem> items = await _shieldDb
            .InventoryItems.Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        Dictionary<Guid, Advisory> advisories = await _feedsDb
            .Advisories.Where(advisory => advisoryIds.Contains(advisory.Id))
            .ToDictionaryAsync(advisory => advisory.Id, ct);

        List<FindingResponse> result = new(findings.Count);
        foreach (Finding finding in findings)
        {
            items.TryGetValue(finding.InventoryItemId, out InventoryItem? item);
            advisories.TryGetValue(finding.AdvisoryRefId, out Advisory? advisory);
            sourceNames.TryGetValue(finding.SourceId, out string? sourceName);
            result.Add(
                FindingResponse.From(
                    finding,
                    sourceName: sourceName,
                    packageName: item?.Name ?? advisory?.PackageName,
                    packageVersion: item?.Version,
                    ecosystem: item?.Ecosystem ?? advisory?.Ecosystem,
                    advisoryExternalId: advisory?.ExternalId,
                    advisorySummary: advisory?.Summary
                )
            );
        }
        return result;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FindingDetailResponse>> Get(Guid id, CancellationToken ct)
    {
        Finding? finding = await _shieldDb.Findings.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (finding is null)
            return NotFound();
        // 404 not 403 so source existence isn't leaked to maintainers without the grant.
        if (!await _access.CanReadAsync(User, finding.SourceId, ct))
            return NotFound();

        Advisory? advisory = await _feedsDb.Advisories.FirstOrDefaultAsync(
            item => item.Id == finding.AdvisoryRefId,
            ct
        );
        InventoryItem? item = await _shieldDb.InventoryItems.FirstOrDefaultAsync(
            entry => entry.Id == finding.InventoryItemId,
            ct
        );
        Source? sourceRecord = await _shieldDb.Sources.FirstOrDefaultAsync(
            source => source.Id == finding.SourceId,
            ct
        );
        SourceType? sourceType = sourceRecord?.Type;

        FixSuggestionResponse? fix = null;
        if (advisory is not null && item is not null)
        {
            FixSuggestion? suggestion = _fixSuggester.Suggest(advisory, item);
            if (suggestion is not null)
                fix = new FixSuggestionResponse(
                    suggestion.PackageName,
                    suggestion.CurrentVersion,
                    suggestion.SuggestedVersion,
                    suggestion.Notes
                );
        }

        FindingResponse findingResponse = FindingResponse.From(
            finding,
            sourceName: sourceRecord?.Name,
            packageName: item?.Name ?? advisory?.PackageName,
            packageVersion: item?.Version,
            ecosystem: item?.Ecosystem ?? advisory?.Ecosystem,
            advisoryExternalId: advisory?.ExternalId,
            advisorySummary: advisory?.Summary
        );

        return Ok(new FindingDetailResponse(findingResponse, advisory, item, sourceType, fix));
    }

    // POST /api/findings/{id}/apply-fix — Body: { strategy: "auto" | "pr" }.
    // auto = manifest edit in place for LocalFolder sources; pr = open a GitHub PR for
    // GithubRepo sources. Mismatched strategy/source pairings return 400.
    // Authorization: Triage on the finding's source (Admin always qualifies).
    [HttpPost("{id:guid}/apply-fix")]
    public async Task<ActionResult<ApplyFixResponse>> ApplyFix(
        Guid id,
        [FromBody] ApplyFixRequest request,
        CancellationToken ct
    )
    {
        Finding? finding = await _shieldDb.Findings.FirstOrDefaultAsync(
            entry => entry.Id == id,
            ct
        );
        if (finding is null)
            return NotFound();
        if (!await _access.CanReadAsync(User, finding.SourceId, ct))
            return NotFound();
        if (!await _access.CanTriageAsync(User, finding.SourceId, ct))
            return Forbid();

        Source? source = await _shieldDb.Sources.FirstOrDefaultAsync(
            entry => entry.Id == finding.SourceId,
            ct
        );
        if (source is null)
            return NotFound();

        InventoryItem? item = await _shieldDb.InventoryItems.FirstOrDefaultAsync(
            entry => entry.Id == finding.InventoryItemId,
            ct
        );
        Advisory? advisory = await _feedsDb.Advisories.FirstOrDefaultAsync(
            entry => entry.Id == finding.AdvisoryRefId,
            ct
        );
        if (item is null || advisory is null)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    Array.Empty<string>(),
                    null,
                    null,
                    "Finding is missing its inventory item or advisory."
                )
            );

        FixSuggestion? suggestion = _fixSuggester.Suggest(advisory, item);
        if (suggestion is null)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    Array.Empty<string>(),
                    null,
                    null,
                    "Advisory has no known fix version greater than the installed version."
                )
            );

        string strategy = (request.Strategy ?? string.Empty).Trim().ToLowerInvariant();
        if (strategy != "auto" && strategy != "pr")
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    Array.Empty<string>(),
                    null,
                    null,
                    "Strategy must be 'auto' or 'pr'."
                )
            );

        if (strategy == "auto" && source.Type != SourceType.LocalFolder)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    Array.Empty<string>(),
                    null,
                    null,
                    "Use strategy=pr for GitHub repo sources."
                )
            );
        if (strategy == "pr" && source.Type != SourceType.GithubRepo)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    Array.Empty<string>(),
                    null,
                    null,
                    "Use strategy=auto for local folder sources."
                )
            );

        ApplyFixResult result =
            strategy == "auto"
                ? await _fixApplier.ApplyLocalAsync(source, item, suggestion, ct)
                : await _fixApplier.ApplyPullRequestAsync(source, item, advisory, suggestion, ct);

        if (!result.Success)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    result.ChangedFiles,
                    result.FollowUpCommand,
                    result.PullRequestUrl,
                    result.Reason
                )
            );

        finding.State = FindingState.Acked;
        finding.Notes = result.PullRequestUrl is not null
            ? $"PR opened: {result.PullRequestUrl}"
            : $"Applied bump to {suggestion.SuggestedVersion}; rescan to verify";
        await _shieldDb.SaveChangesAsync(ct);

        return Ok(
            new ApplyFixResponse(
                result.Success,
                result.ChangedFiles,
                result.FollowUpCommand,
                result.PullRequestUrl,
                result.Reason
            )
        );
    }

    [HttpPost("{id:guid}/ack")]
    public Task<IActionResult> Ack(Guid id, CancellationToken ct) =>
        UpdateStateAsync(id, FindingState.Acked, null, ct);

    [HttpPost("{id:guid}/resolve")]
    public Task<IActionResult> Resolve(Guid id, CancellationToken ct) =>
        UpdateStateAsync(id, FindingState.Resolved, null, ct);

    [HttpPost("{id:guid}/suppress")]
    public Task<IActionResult> Suppress(
        Guid id,
        [FromBody] SuppressFindingRequest request,
        CancellationToken ct
    ) => UpdateStateAsync(id, FindingState.Suppressed, request.Reason, ct);

    private async Task<IActionResult> UpdateStateAsync(
        Guid id,
        FindingState state,
        string? notes,
        CancellationToken ct
    )
    {
        Finding? finding = await _shieldDb.Findings.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (finding is null)
            return NotFound();

        finding.State = state;
        if (notes is not null)
            finding.Notes = notes;
        await _shieldDb.SaveChangesAsync(ct);
        return Ok(FindingResponse.From(finding));
    }

    [HttpPost("bulk-ack")]
    public Task<ActionResult<BulkFindingsResponse>> BulkAck(
        [FromBody] BulkFindingsRequest request,
        CancellationToken ct
    ) => BulkUpdateStateAsync(request.FindingIds, FindingState.Acked, notes: null, ct);

    [HttpPost("bulk-resolve")]
    public Task<ActionResult<BulkFindingsResponse>> BulkResolve(
        [FromBody] BulkFindingsRequest request,
        CancellationToken ct
    ) => BulkUpdateStateAsync(request.FindingIds, FindingState.Resolved, notes: null, ct);

    [HttpPost("bulk-suppress")]
    public Task<ActionResult<BulkFindingsResponse>> BulkSuppress(
        [FromBody] BulkSuppressRequest request,
        CancellationToken ct
    ) => BulkUpdateStateAsync(request.FindingIds, FindingState.Suppressed, request.Reason, ct);

    private async Task<ActionResult<BulkFindingsResponse>> BulkUpdateStateAsync(
        IReadOnlyList<Guid>? ids,
        FindingState state,
        string? notes,
        CancellationToken ct
    )
    {
        if (ids is null || ids.Count == 0)
            return BadRequest(new { error = "findingIds must be non-empty." });
        if (ids.Count > MaxBulkSize)
            return BadRequest(new { error = $"findingIds exceeds maximum of {MaxBulkSize}." });

        // Dedup while preserving the caller's order for the notFound diff below.
        List<Guid> distinctIds = ids.Distinct().ToList();

        List<Finding> matched = await _shieldDb
            .Findings.Where(finding => distinctIds.Contains(finding.Id))
            .ToListAsync(ct);

        HashSet<Guid> foundIds = matched.Select(finding => finding.Id).ToHashSet();
        List<Guid> notFound = distinctIds.Where(id => !foundIds.Contains(id)).ToList();

        foreach (Finding finding in matched)
        {
            finding.State = state;
            if (notes is not null)
                finding.Notes = notes;
        }

        if (matched.Count > 0)
            await _shieldDb.SaveChangesAsync(ct);

        return Ok(new BulkFindingsResponse(matched.Count, notFound));
    }
}
