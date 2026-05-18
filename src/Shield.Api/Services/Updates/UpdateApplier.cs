using System.Text;
using Shield.Api.Services.Ecosystems;
using Shield.Api.Services.ManifestEditors;
using Shield.Api.Services.PullRequests;
using Shield.Api.Services.SourceFs;

namespace Shield.Api.Services.Updates;

public sealed class UpdateApplier : IUpdateApplier
{
    private readonly ShieldDbContext _db;
    private readonly IEcosystemRegistry _ecosystems;
    private readonly IRepoSourceFs _sourceFs;
    private readonly IRepoPullRequestOpener _prOpener;
    private readonly ILogger<UpdateApplier> _log;

    public UpdateApplier(
        ShieldDbContext db,
        IEcosystemRegistry ecosystems,
        IRepoSourceFs sourceFs,
        IRepoPullRequestOpener prOpener,
        ILogger<UpdateApplier> log
    )
    {
        _db = db;
        _ecosystems = ecosystems;
        _sourceFs = sourceFs;
        _prOpener = prOpener;
        _log = log;
    }

    public async Task<UpdateApplyResult> ApplyAsync(
        UpdateApplyRequest request,
        Func<SourceApplyOutcome, Task>? onSourceCompleted,
        CancellationToken ct
    )
    {
        IQueryable<PackageUpdate> rowsQuery = _db.PackageUpdates.Where(update =>
            update.AppliedAt == null
        );
        if (request.SourceIds is { Count: > 0 } sourceIds)
            rowsQuery = rowsQuery.Where(update => sourceIds.Contains(update.SourceId));

        List<PackageUpdate> rows = await rowsQuery.ToListAsync(ct);
        if (rows.Count == 0)
            return new(Sources: []);

        Dictionary<int, Source> sourcesById = await _db
            .Sources.Where(source =>
                rows.Select(row => row.SourceId).Distinct().Contains(source.Id)
            )
            .ToDictionaryAsync(source => source.Id, ct);

        List<SourceApplyOutcome> outcomes = [];
        DateTime now = DateTime.UtcNow;

        foreach (IGrouping<int, PackageUpdate> sourceGroup in rows.GroupBy(row => row.SourceId))
        {
            if (!sourcesById.TryGetValue(sourceGroup.Key, out Source? source))
                continue;
            ct.ThrowIfCancellationRequested();

            SourceApplyOutcome outcome = await ApplySingleSourceAsync(
                source,
                [.. sourceGroup],
                request,
                now,
                ct
            );
            outcomes.Add(outcome);
            if (onSourceCompleted is not null)
                await onSourceCompleted(outcome);
        }

        return new(Sources: outcomes);
    }

    private async Task<SourceApplyOutcome> ApplySingleSourceAsync(
        Source source,
        IReadOnlyList<PackageUpdate> sourceRows,
        UpdateApplyRequest request,
        DateTime now,
        CancellationToken ct
    )
    {
        if (source.Type != SourceType.GithubRepo)
            return Skip(source, "Source is not a GithubRepo — Updates apply requires GitHub.");
        if (source.IsProduction && !request.DryRun && !request.ConfirmProduction)
            return Skip(source, "Production-source confirmation required.");

        // Per-row filters.
        HashSet<(Ecosystem, string)> advisoryDriven = await OpenFindingPackagesAsync(source.Id, ct);
        int skippedYoung = 0;
        int skippedMajor = 0;
        List<PackageUpdate> selected = [];
        foreach (PackageUpdate row in sourceRows)
        {
            if (request.Scope == UpdateApplyScope.LatestMinor && row.IsBreakingMajor)
            {
                skippedMajor++;
                continue;
            }
            bool advisory = advisoryDriven.Contains((row.Ecosystem, row.Name));
            if (
                !advisory
                && !request.Force
                && source.MinPackageAgeHours > 0
                && row.PublishedAt.HasValue
                && (now - row.PublishedAt.Value) < TimeSpan.FromHours(source.MinPackageAgeHours)
            )
            {
                skippedYoung++;
                continue;
            }
            selected.Add(row);
        }

        if (selected.Count == 0)
            return new(
                source.Id,
                source.Name,
                PullRequestUrl: null,
                BumpedCount: 0,
                SkippedYoungCount: skippedYoung,
                SkippedMajorCount: skippedMajor,
                Errors: []
            );

        // Build a working tree, apply each bump through the package's IEcosystem manifest editor.
        string workRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-updates",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(workRoot);

        List<string> errors = [];
        try
        {
            HashSet<string> touchedManifests = [];
            // Cache fetched manifest paths so multiple bumps targeting the same package.json /
            // composer.json don't round-trip GitHub repeatedly.
            HashSet<string> fetchedPaths = [];

            foreach (PackageUpdate row in selected)
            {
                IEcosystem? ecosystem = _ecosystems.For(row.Ecosystem);
                if (ecosystem is null || !ecosystem.SupportsAutomaticPullRequests)
                {
                    errors.Add($"{row.Name}: {row.Ecosystem} doesn't support automatic PRs.");
                    continue;
                }

                InventoryItem item = await BuildInventoryItemAsync(row, ecosystem, ct);
                string manifestRelative = string.IsNullOrWhiteSpace(item.ManifestPath)
                    ? ecosystem.DefaultManifestPath
                    : item.ManifestPath;

                if (fetchedPaths.Add(manifestRelative))
                {
                    string? content = await _sourceFs.ReadFileAsync(source, manifestRelative, ct);
                    if (content is null)
                    {
                        errors.Add($"{row.Name}: manifest '{manifestRelative}' not found in repo.");
                        continue;
                    }
                    string localPath = Path.Combine(
                        workRoot,
                        manifestRelative.Replace('/', Path.DirectorySeparatorChar)
                    );
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await File.WriteAllTextAsync(localPath, content, ct);
                }

                ManifestEditOutcome edit = ecosystem.Apply(workRoot, item, row.LatestVersion);
                if (edit.UnsupportedReason is not null)
                {
                    errors.Add($"{row.Name}: {edit.UnsupportedReason}");
                    continue;
                }
                foreach (string changed in edit.ChangedFiles)
                {
                    string relative = Path.GetRelativePath(workRoot, changed).Replace('\\', '/');
                    touchedManifests.Add(relative);
                }
            }

            if (touchedManifests.Count == 0)
                return new(
                    source.Id,
                    source.Name,
                    PullRequestUrl: null,
                    BumpedCount: 0,
                    SkippedYoungCount: skippedYoung,
                    SkippedMajorCount: skippedMajor,
                    Errors: errors.Count == 0 ? ["No manifest files were modified."] : errors
                );

            if (request.DryRun)
                return new(
                    source.Id,
                    source.Name,
                    PullRequestUrl: null,
                    BumpedCount: selected.Count,
                    SkippedYoungCount: skippedYoung,
                    SkippedMajorCount: skippedMajor,
                    Errors: errors
                );

            // Read final file contents, hand to the PR opener.
            List<RepoFileEdit> edits = [];
            foreach (string relative in touchedManifests)
            {
                string localPath = Path.Combine(
                    workRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)
                );
                if (!File.Exists(localPath))
                    continue;
                edits.Add(new(relative, File.ReadAllText(localPath)));
            }

            RepoPullRequestSpec spec = BuildSpec(selected, request.Scope);
            RepoPullRequestResult prResult = await _prOpener.OpenAsync(source, edits, spec, ct);

            if (prResult.PullRequestUrl is not null)
            {
                DateTime appliedAt = DateTime.UtcNow;
                foreach (PackageUpdate row in selected)
                {
                    row.AppliedAt = appliedAt;
                    row.AppliedPullRequestUrl = prResult.PullRequestUrl;
                }
                await _db.SaveChangesAsync(ct);
            }

            return new(
                source.Id,
                source.Name,
                PullRequestUrl: prResult.PullRequestUrl,
                BumpedCount: selected.Count,
                SkippedYoungCount: skippedYoung,
                SkippedMajorCount: skippedMajor,
                Errors: [.. errors, .. prResult.Errors.Select(error => error.Message)]
            );
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update apply failed for source {SourceId}", source.Id);
            return new(
                source.Id,
                source.Name,
                PullRequestUrl: null,
                BumpedCount: 0,
                SkippedYoungCount: skippedYoung,
                SkippedMajorCount: skippedMajor,
                Errors: [ex.Message]
            );
        }
        finally
        {
            try
            {
                Directory.Delete(workRoot, recursive: true);
            }
            catch
            { /* best-effort */
            }
        }
    }

    private async Task<HashSet<(Ecosystem, string)>> OpenFindingPackagesAsync(
        int sourceId,
        CancellationToken ct
    )
    {
        List<InventoryItem> items = await (
            from finding in _db.Findings
            join item in _db.InventoryItems on finding.InventoryItemId equals item.Id
            where finding.SourceId == sourceId && finding.State == FindingState.Open
            select item
        ).ToListAsync(ct);
        return items.Select(item => (item.Ecosystem, item.Name)).ToHashSet();
    }

    private async Task<InventoryItem> BuildInventoryItemAsync(
        PackageUpdate row,
        IEcosystem ecosystem,
        CancellationToken ct
    )
    {
        if (row.InventoryItemId.HasValue)
        {
            InventoryItem? real = await _db.InventoryItems.FirstOrDefaultAsync(
                item => item.Id == row.InventoryItemId.Value,
                ct
            );
            if (real is not null)
                return real;
        }
        return new()
        {
            Ecosystem = row.Ecosystem,
            Name = row.Name,
            Version = row.CurrentVersion,
            IsDirect = true,
            ManifestPath = ecosystem.DefaultManifestPath,
        };
    }

    private static RepoPullRequestSpec BuildSpec(
        IReadOnlyList<PackageUpdate> bumps,
        UpdateApplyScope scope
    )
    {
        string scopeLabel = scope == UpdateApplyScope.LatestMinor ? "minor" : "latest";
        string title = $"chore(deps): bump {bumps.Count} packages to {scopeLabel}";
        StringBuilder body = new();
        body.AppendLine($"Automated dependency bump by Shield ({scopeLabel} scope).\n");
        foreach (PackageUpdate bump in bumps.OrderBy(b => b.Ecosystem).ThenBy(b => b.Name))
        {
            body.AppendLine(
                $"- `{bump.Name}` `{bump.CurrentVersion}` → `{bump.LatestVersion}` ({bump.Ecosystem})"
            );
        }
        body.AppendLine();
        body.AppendLine("---");
        body.AppendLine(
            "Generated by [Shield](https://github.com/nomercylabs/shield). Review before merging."
        );
        return new(
            BranchPrefix: $"shield/updates-{scopeLabel}",
            CommitMessage: title,
            PrTitle: title,
            PrBody: body.ToString().TrimEnd()
        );
    }

    private static SourceApplyOutcome Skip(Source source, string reason) =>
        new(
            source.Id,
            source.Name,
            PullRequestUrl: null,
            BumpedCount: 0,
            SkippedYoungCount: 0,
            SkippedMajorCount: 0,
            Errors: [reason]
        );
}
