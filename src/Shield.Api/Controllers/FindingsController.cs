using Shield.Api.Services.ManifestEditors;

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
    private readonly Services.Ecosystems.IEcosystemRegistry _ecosystems;

    public FindingsController(
        ShieldDbContext shieldDb,
        FeedsDbContext feedsDb,
        IFixSuggester fixSuggester,
        IFixApplier fixApplier,
        IAccessResolver access,
        Services.Ecosystems.IEcosystemRegistry ecosystems
    )
    {
        _shieldDb = shieldDb;
        _feedsDb = feedsDb;
        _fixSuggester = fixSuggester;
        _fixApplier = fixApplier;
        _access = access;
        _ecosystems = ecosystems;
    }

    [HttpGet]
    [RequireApiScope("findings:read")]
    public async Task<ActionResult<FindingsPage>> List(
        [FromQuery] List<Severity>? severity,
        [FromQuery] List<int>? sourceId,
        [FromQuery] List<Ecosystem>? ecosystem,
        [FromQuery] List<FindingState>? state,
        [FromQuery] List<string>? packageName,
        [FromQuery] bool? hasFix,
        [FromQuery] bool? kevOnly,
        [FromQuery] decimal? epssMin,
        [FromQuery] string? advisoryQuery,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
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

        // Pre-fetch advisory ID sets from FeedsDb for the KEV/EPSS/advisoryQuery filters.
        // These tables are small enough to hold in memory; one round-trip each is cheaper than
        // a cross-context join.
        HashSet<Guid>? kevAdvisoryIds = null;
        if (kevOnly == true)
        {
            kevAdvisoryIds = (
                await _feedsDb
                    .Advisories.Where(advisory => advisory.IsKev)
                    .Select(advisory => advisory.Id)
                    .ToListAsync(ct)
            ).ToHashSet();
        }

        HashSet<Guid>? epssAdvisoryIds = null;
        if (epssMin is > 0)
        {
            double epssThreshold = (double)epssMin.Value;
            epssAdvisoryIds = (
                await _feedsDb
                    .Advisories.Where(advisory =>
                        advisory.EpssScore != null && advisory.EpssScore >= epssThreshold
                    )
                    .Select(advisory => advisory.Id)
                    .ToListAsync(ct)
            ).ToHashSet();
        }

        HashSet<Guid>? advisoryQueryIds = null;
        string? trimmedAdvisoryQuery = advisoryQuery?.Trim();
        if (!string.IsNullOrEmpty(trimmedAdvisoryQuery))
        {
            string pattern = "%" + trimmedAdvisoryQuery + "%";
            advisoryQueryIds = (
                await _feedsDb
                    .Advisories.Where(advisory => EF.Functions.Like(advisory.ExternalId, pattern))
                    .Select(advisory => advisory.Id)
                    .ToListAsync(ct)
            ).ToHashSet();
        }

        IQueryable<Finding> query = _shieldDb.Findings.AsQueryable();

        // Per-source ACL — non-Admins only see findings on sources they were granted.
        if (!User.IsInRole(ShieldRoles.Admin))
        {
            IReadOnlyList<int> visible = await _access.GetVisibleSourceIdsAsync(User, ct);
            if (visible.Count == 0)
                return Ok(new FindingsPage([], 0, page, pageSize));
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

        if (kevAdvisoryIds is not null)
            query = query.Where(finding => kevAdvisoryIds.Contains(finding.AdvisoryRefId));

        if (epssAdvisoryIds is not null)
            query = query.Where(finding => epssAdvisoryIds.Contains(finding.AdvisoryRefId));

        if (advisoryQueryIds is not null)
            query = query.Where(finding => advisoryQueryIds.Contains(finding.AdvisoryRefId));

        // Apply sort before paging. Unknown sort values fall back to severity desc.
        string sort = (sortBy ?? "severity").Trim().ToLowerInvariant();
        bool descending = (sortDir ?? "desc").Trim().ToLowerInvariant() != "asc";

        query = sort switch
        {
            "discoveredat" => descending
                ? query.OrderByDescending(finding => finding.FirstSeenAt)
                : query.OrderBy(finding => finding.FirstSeenAt),
            "packagename" => descending
                ? query.OrderByDescending(finding => finding.InventoryItemId)
                : query.OrderBy(finding => finding.InventoryItemId),
            "sourcename" => descending
                ? query.OrderByDescending(finding => finding.SourceId)
                : query.OrderBy(finding => finding.SourceId),
            _ => descending
                ? query.OrderByDescending(finding => finding.Severity)
                : query.OrderBy(finding => finding.Severity),
        };

        int total = await query.CountAsync(ct);
        List<Finding> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        List<FindingResponse> enriched;
        if (hasFix.HasValue)
        {
            // hasFix requires the real FixSuggester, which needs advisory + item.
            // EnrichWithContextAsync returns the context alongside each response so we
            // can run the suggester without a second DB round-trip.
            List<(FindingResponse Response, Advisory? Advisory, InventoryItem? Item)> withContext =
                await EnrichWithContextAsync(items, ct);
            // Build the per-(ecosystem, packageName) advisory set once so the suggester can
            // pick the highest fix across all advisories for the same package.
            HashSet<(Ecosystem, string)> packageKeys = withContext
                .Where(tuple => tuple.Item is not null)
                .Select(tuple => (tuple.Item!.Ecosystem, tuple.Item.Name))
                .ToHashSet();
            Dictionary<(Ecosystem, string), List<Advisory>> advisoriesByPackage =
                await LoadAdvisoriesByPackageAsync(packageKeys, ct);

            enriched = withContext
                .Where(tuple =>
                {
                    if (tuple.Item is null)
                        return !hasFix.Value;
                    (Ecosystem eco, string name) = (tuple.Item.Ecosystem, tuple.Item.Name);
                    advisoriesByPackage.TryGetValue(
                        (eco, name),
                        out List<Advisory>? packageAdvisories
                    );
                    bool suggestion =
                        packageAdvisories is { Count: > 0 }
                        && _fixSuggester.SuggestForPackage(
                            eco,
                            name,
                            tuple.Item.Version,
                            packageAdvisories
                        )
                            is not null;
                    return hasFix.Value ? suggestion : !suggestion;
                })
                .Select(tuple => tuple.Response)
                .ToList();
        }
        else
        {
            enriched = await EnrichAsync(items, ct);
        }

        return Ok(new FindingsPage(enriched, total, page, pageSize));
    }

    private async Task<List<FindingResponse>> EnrichAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken ct
    )
    {
        List<(FindingResponse Response, Advisory? Advisory, InventoryItem? Item)> withContext =
            await EnrichWithContextAsync(findings, ct);
        return withContext.Select(tuple => tuple.Response).ToList();
    }

    private async Task<
        List<(FindingResponse Response, Advisory? Advisory, InventoryItem? Item)>
    > EnrichWithContextAsync(IReadOnlyList<Finding> findings, CancellationToken ct)
    {
        if (findings.Count == 0)
            return [];

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

        List<(FindingResponse, Advisory?, InventoryItem?)> result = new(findings.Count);
        foreach (Finding finding in findings)
        {
            items.TryGetValue(finding.InventoryItemId, out InventoryItem? item);
            advisories.TryGetValue(finding.AdvisoryRefId, out Advisory? advisory);
            sourceNames.TryGetValue(finding.SourceId, out string? sourceName);
            result.Add(
                (
                    FindingResponse.From(
                        finding,
                        sourceName: sourceName,
                        packageName: item?.Name ?? advisory?.PackageName,
                        packageVersion: item?.Version,
                        ecosystem: item?.Ecosystem ?? advisory?.Ecosystem,
                        advisoryExternalId: advisory?.ExternalId,
                        advisorySummary: advisory?.Summary
                    ),
                    advisory,
                    item
                )
            );
        }
        return result;
    }

    [HttpGet("{id:guid}")]
    [RequireApiScope("findings:read")]
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
        if (item is not null)
        {
            List<Advisory> packageAdvisories = await _feedsDb
                .Advisories.Where(advisoryRow =>
                    advisoryRow.Ecosystem == item.Ecosystem && advisoryRow.PackageName == item.Name
                )
                .ToListAsync(ct);

            FixSuggestion? suggestion = _fixSuggester.SuggestForPackage(
                item.Ecosystem,
                item.Name,
                item.Version,
                packageAdvisories
            );
            if (suggestion is not null)
            {
                FixEligibility prEligibility = ComputePrEligibility(sourceRecord, item);
                FixEligibility autoEligibility = ComputeAutoEligibility(sourceRecord, item);
                fix = new(
                    suggestion.PackageName,
                    suggestion.CurrentVersion,
                    suggestion.SuggestedVersion,
                    suggestion.Notes,
                    prEligibility,
                    autoEligibility
                );
            }
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

        bool canTriage = await _access.CanTriageAsync(User, finding.SourceId, ct);
        return Ok(
            new FindingDetailResponse(findingResponse, advisory, item, sourceType, fix, canTriage)
        );
    }

    // POST /api/findings/{id}/apply-fix — Body: { strategy: "auto" | "pr" }.
    // auto = manifest edit in place for LocalFolder sources; pr = open a GitHub PR for
    // GithubRepo sources. Mismatched strategy/source pairings return 400.
    // Authorization: Maintainer-or-Admin policy gates Viewers at 403; per-source Triage
    // check inside narrows Maintainers to sources they actually own.
    [HttpPost("{id:guid}/apply-fix")]
    [Authorize(Policy = "MaintainerOrAdmin")]
    [NoApiToken]
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
                    [],
                    null,
                    null,
                    "Finding is missing its inventory item or advisory."
                )
            );

        List<Advisory> allPackageAdvisories = await _feedsDb
            .Advisories.Where(advisoryRow =>
                advisoryRow.Ecosystem == item.Ecosystem && advisoryRow.PackageName == item.Name
            )
            .ToListAsync(ct);

        FixSuggestion? suggestion = _fixSuggester.SuggestForPackage(
            item.Ecosystem,
            item.Name,
            item.Version,
            allPackageAdvisories
        );
        if (suggestion is null)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    [],
                    null,
                    null,
                    "Advisory has no known fix version greater than the installed version."
                )
            );

        string strategy = (request.Strategy ?? string.Empty).Trim().ToLowerInvariant();
        if (strategy != "auto" && strategy != "pr")
            return BadRequest(
                new ApplyFixResponse(false, [], null, null, "Strategy must be 'auto' or 'pr'.")
            );

        if (strategy == "auto" && source.Type != SourceType.LocalFolder)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    [],
                    null,
                    null,
                    "Use strategy=pr for GitHub repo sources."
                )
            );
        if (strategy == "pr" && source.Type != SourceType.GithubRepo)
            return BadRequest(
                new ApplyFixResponse(
                    false,
                    [],
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
                result.Reason,
                result.CleanedFiles,
                result.CleanedDirectories
            )
        );
    }

    [HttpPost("{id:guid}/ack")]
    [RequireApiScope("findings:write")]
    public Task<IActionResult> Ack(Guid id, CancellationToken ct) =>
        UpdateStateAsync(id, FindingState.Acked, null, ct);

    [HttpPost("{id:guid}/resolve")]
    [RequireApiScope("findings:write")]
    public Task<IActionResult> Resolve(Guid id, CancellationToken ct) =>
        UpdateStateAsync(id, FindingState.Resolved, null, ct);

    [HttpPost("{id:guid}/suppress")]
    [RequireApiScope("findings:write")]
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
        // 404 (not 403) for sources the maintainer can't even see; 403 when they can read
        // but lack Triage. Admins short-circuit both checks inside the resolver.
        if (!await _access.CanReadAsync(User, finding.SourceId, ct))
            return NotFound();
        if (!await _access.CanTriageAsync(User, finding.SourceId, ct))
            return Forbid();

        finding.State = state;
        if (notes is not null)
            finding.Notes = notes;
        await _shieldDb.SaveChangesAsync(ct);
        return Ok(FindingResponse.From(finding));
    }

    [HttpPost("bulk-ack")]
    [RequireApiScope("findings:write")]
    public Task<ActionResult<BulkFindingsResponse>> BulkAck(
        [FromBody] BulkFindingsRequest request,
        CancellationToken ct
    ) => BulkUpdateStateAsync(request.FindingIds, FindingState.Acked, notes: null, ct);

    [HttpPost("bulk-resolve")]
    [RequireApiScope("findings:write")]
    public Task<ActionResult<BulkFindingsResponse>> BulkResolve(
        [FromBody] BulkFindingsRequest request,
        CancellationToken ct
    ) => BulkUpdateStateAsync(request.FindingIds, FindingState.Resolved, notes: null, ct);

    [HttpPost("bulk-suppress")]
    [RequireApiScope("findings:write")]
    public Task<ActionResult<BulkFindingsResponse>> BulkSuppress(
        [FromBody] BulkSuppressRequest request,
        CancellationToken ct
    ) => BulkUpdateStateAsync(request.FindingIds, FindingState.Suppressed, request.Reason, ct);

    private async Task<
        Dictionary<(Ecosystem, string), List<Advisory>>
    > LoadAdvisoriesByPackageAsync(
        IReadOnlyCollection<(Ecosystem Ecosystem, string Name)> packageKeys,
        CancellationToken ct
    )
    {
        if (packageKeys.Count == 0)
            return [];

        // Pull all advisories whose (ecosystem, packageName) matches any key in the set.
        // Two-step: ecosystems in, then filter in-memory on name — avoids non-translatable
        // ValueTuple comparisons in EF Core SQLite.
        HashSet<Ecosystem> ecosystems = packageKeys.Select(key => key.Ecosystem).ToHashSet();
        HashSet<string> names = packageKeys.Select(key => key.Name).ToHashSet();
        List<Advisory> rows = await _feedsDb
            .Advisories.Where(advisoryRow =>
                ecosystems.Contains(advisoryRow.Ecosystem)
                && names.Contains(advisoryRow.PackageName)
            )
            .ToListAsync(ct);

        Dictionary<(Ecosystem, string), List<Advisory>> result = [];
        foreach (Advisory advisoryRow in rows)
        {
            (Ecosystem, string) key = (advisoryRow.Ecosystem, advisoryRow.PackageName);
            if (!packageKeys.Contains(key))
                continue;
            if (!result.TryGetValue(key, out List<Advisory>? bucket))
            {
                bucket = [];
                result[key] = bucket;
            }
            bucket.Add(advisoryRow);
        }
        return result;
    }

    private FixEligibility ComputePrEligibility(Source? source, InventoryItem item)
    {
        if (source is null || source.Type != SourceType.GithubRepo)
            return new(false, "PR strategy needs a GithubRepo source.");
        Services.Ecosystems.IEcosystem? ecosystem = _ecosystems.For(item.Ecosystem);
        if (ecosystem is null || !ecosystem.SupportsAutomaticPullRequests)
            return new(false, $"Ecosystem {item.Ecosystem} doesn't support automated PRs.");
        return new(true, null);
    }

    private FixEligibility ComputeAutoEligibility(Source? source, InventoryItem item)
    {
        if (source is null || source.Type != SourceType.LocalFolder)
            return new(false, "Auto-apply only works for LocalFolder sources.");
        Services.Ecosystems.IEcosystem? ecosystem = _ecosystems.For(item.Ecosystem);
        if (ecosystem is null || !ecosystem.SupportsAutomaticPullRequests)
            return new(false, $"No editor for ecosystem {item.Ecosystem}.");
        if (string.IsNullOrWhiteSpace(source.ConfigJson))
            return new(false, "Path missing.");
        return new(true, null);
    }

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

        // Treat findings on un-triageable sources as notFound — same leak-prevention rule
        // as the per-id endpoints (read-without-triage isn't surfaced separately on bulk).
        if (!User.IsInRole(ShieldRoles.Admin))
        {
            List<Finding> triageable = new(matched.Count);
            foreach (Finding finding in matched)
            {
                if (await _access.CanTriageAsync(User, finding.SourceId, ct))
                    triageable.Add(finding);
            }
            matched = triageable;
        }

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
