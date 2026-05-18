using System.Globalization;
using System.Text;
using System.Text.Json;
using Shield.Api.Services.Ecosystems;
using Shield.Api.Services.ManifestEditors;
using Shield.Api.Services.PullRequests;
using Shield.Api.Services.SourceFs;

namespace Shield.Api.Services.BulkFix;

public sealed class BulkFixApplier : IBulkFixApplier
{
    private readonly ShieldDbContext _db;
    private readonly FeedsDbContext _feedsDb;
    private readonly IFixSuggester _suggester;
    private readonly IEcosystemRegistry _ecosystems;
    private readonly IRepoSourceFs _sourceFs;
    private readonly IRepoPullRequestOpener _prOpener;

    public BulkFixApplier(
        ShieldDbContext db,
        FeedsDbContext feedsDb,
        IFixSuggester suggester,
        IEcosystemRegistry ecosystems,
        IRepoSourceFs sourceFs,
        IRepoPullRequestOpener prOpener
    )
    {
        _db = db;
        _feedsDb = feedsDb;
        _suggester = suggester;
        _ecosystems = ecosystems;
        _sourceFs = sourceFs;
        _prOpener = prOpener;
    }

    public async Task<BulkApplyResult> ApplyAllPullRequestAsync(
        Source source,
        IReadOnlyList<Advisory> allAdvisories,
        bool dryRun,
        int? maxPackages,
        bool allowMajorBumps,
        CancellationToken ct
    )
    {
        if (source.Type != SourceType.GithubRepo)
        {
            return new(
                DryRun: dryRun,
                PullRequestUrl: null,
                Entries: [],
                Errors: [new("(source)", "Bulk PR strategy requires a GithubRepo source.")],
                ReusedBranch: null,
                MajorBumps: [],
                Warnings: []
            );
        }

        // Config validation lives in IRepoSourceFs / IRepoPullRequestOpener — they both fail
        // gracefully when owner/repo/token is missing, so no need to pre-check here.

        // Load all Open findings for this source, joined to inventory items.
        List<Finding> openFindings = await _db
            .Findings.Where(finding =>
                finding.SourceId == source.Id && finding.State == FindingState.Open
            )
            .ToListAsync(ct);

        if (openFindings.Count == 0)
        {
            return new(
                DryRun: dryRun,
                PullRequestUrl: null,
                Entries: [],
                Errors: [],
                ReusedBranch: null,
                MajorBumps: [],
                Warnings: []
            );
        }

        HashSet<int> itemIds = openFindings.Select(finding => finding.InventoryItemId).ToHashSet();
        Dictionary<int, InventoryItem> itemsById = await _db
            .InventoryItems.Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);

        // Group findings by (ecosystem, packageName) — one bump per package kills all advisories.
        Dictionary<(Ecosystem, string), List<Finding>> byPackage = new();
        foreach (Finding finding in openFindings)
        {
            if (!itemsById.TryGetValue(finding.InventoryItemId, out InventoryItem? item))
                continue;
            (Ecosystem, string) key = (item.Ecosystem, item.Name);
            if (!byPackage.TryGetValue(key, out List<Finding>? bucket))
            {
                bucket = [];
                byPackage[key] = bucket;
            }
            bucket.Add(finding);
        }

        // Pre-load all relevant advisories from FeedsDb in one shot.
        HashSet<Ecosystem> ecosystems = byPackage.Keys.Select(key => key.Item1).ToHashSet();
        HashSet<string> names = byPackage.Keys.Select(key => key.Item2).ToHashSet();
        List<Advisory> feedAdvisories = await _feedsDb
            .Advisories.Where(advisory =>
                ecosystems.Contains(advisory.Ecosystem) && names.Contains(advisory.PackageName)
            )
            .ToListAsync(ct);

        Dictionary<(Ecosystem, string), List<Advisory>> advisoriesByPackage = new();
        foreach (Advisory advisory in feedAdvisories)
        {
            (Ecosystem, string) key = (advisory.Ecosystem, advisory.PackageName);
            if (!advisoriesByPackage.TryGetValue(key, out List<Advisory>? bucket))
            {
                bucket = [];
                advisoriesByPackage[key] = bucket;
            }
            bucket.Add(advisory);
        }

        List<BulkApplyEntry> entries = [];
        List<BulkApplyEntry> majorBumps = [];
        List<BulkApplyError> errors = [];

        foreach (KeyValuePair<(Ecosystem Eco, string Name), List<Finding>> kv in byPackage)
        {
            (Ecosystem eco, string packageName) = kv.Key;

            IEcosystem? ecosystem = _ecosystems.For(eco);
            if (ecosystem is null || !ecosystem.SupportsAutomaticPullRequests)
            {
                errors.Add(new(packageName, $"Ecosystem {eco} doesn't support automated PRs."));
                continue;
            }

            if (
                !itemsById.TryGetValue(
                    kv.Value[0].InventoryItemId,
                    out InventoryItem? representativeItem
                )
            )
            {
                errors.Add(new(packageName, "Inventory item not found."));
                continue;
            }

            advisoriesByPackage.TryGetValue(kv.Key, out List<Advisory>? packageAdvisories);
            if (packageAdvisories is null || packageAdvisories.Count == 0)
            {
                errors.Add(new(packageName, "No advisory data found for package."));
                continue;
            }

            FixSuggestion? suggestion = _suggester.SuggestForPackage(
                eco,
                packageName,
                representativeItem.Version,
                packageAdvisories
            );
            if (suggestion is null)
            {
                errors.Add(new(packageName, "No fix version available above current version."));
                continue;
            }

            string manifestPath = !string.IsNullOrWhiteSpace(representativeItem.ManifestPath)
                ? representativeItem.ManifestPath
                : ecosystem.DefaultManifestPath;

            List<string> coveredAdvisoryIds = packageAdvisories
                .Select(advisory => advisory.ExternalId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            BulkApplyEntry entry = new(
                PackageName: packageName,
                CurrentVersion: representativeItem.Version,
                SuggestedVersion: suggestion.SuggestedVersion,
                ManifestPath: manifestPath,
                AdvisoryIds: coveredAdvisoryIds,
                Ecosystem: eco
            );

            // Major-version bump detection.
            if (SemVerHelper.IsMajorBump(representativeItem.Version, suggestion.SuggestedVersion))
            {
                majorBumps.Add(entry);
                if (!allowMajorBumps)
                    continue;
            }

            entries.Add(entry);
        }

        // Young-package warnings — informational only. EVERY entry here is advisory-driven, so
        // a 30-minute-old fix version is still the right thing to ship. The warning surfaces in
        // the modal so the operator can spot a brand-new "fix" that arrived suspiciously fast.
        // Non-blocking by design: see source.MinPackageAgeHours docs.
        List<BulkApplyWarning> warnings = [];
        if (entries.Count > 0 && source.MinPackageAgeHours > 0)
        {
            DateTime ageCutoff = DateTime.UtcNow - TimeSpan.FromHours(source.MinPackageAgeHours);
            HashSet<Ecosystem> ecoSet = entries.Select(entry => entry.Ecosystem).ToHashSet();
            HashSet<string> nameSet = entries.Select(entry => entry.PackageName).ToHashSet();
            HashSet<string> versionSet = entries
                .Select(entry => entry.SuggestedVersion)
                .ToHashSet();
            List<PackageMeta> metas = await _feedsDb
                .PackageMetas.Where(meta =>
                    ecoSet.Contains(meta.Ecosystem)
                    && nameSet.Contains(meta.Name)
                    && versionSet.Contains(meta.Version)
                )
                .ToListAsync(ct);
            Dictionary<(Ecosystem, string, string), PackageMeta> metaByKey = metas.ToDictionary(
                meta => (meta.Ecosystem, meta.Name, meta.Version)
            );
            foreach (BulkApplyEntry entry in entries)
            {
                if (
                    metaByKey.TryGetValue(
                        (entry.Ecosystem, entry.PackageName, entry.SuggestedVersion),
                        out PackageMeta? meta
                    )
                    && meta.PublishedAt.HasValue
                    && meta.PublishedAt.Value > ageCutoff
                )
                {
                    TimeSpan age = DateTime.UtcNow - meta.PublishedAt.Value;
                    warnings.Add(
                        new(
                            entry.PackageName,
                            $"Suggested version {entry.SuggestedVersion} was published {FormatAge(age)} ago — review before merging. Bumping anyway because advisory(s) {string.Join(", ", entry.AdvisoryIds)} cover the current version."
                        )
                    );
                }
            }
        }

        // Cap to top-N by severity when maxPackages is set. Severity comes from the finding.
        if (maxPackages.HasValue && maxPackages.Value > 0 && entries.Count > maxPackages.Value)
        {
            Dictionary<string, Severity> maxSeverityByPackage = new();
            foreach (Finding finding in openFindings)
            {
                if (!itemsById.TryGetValue(finding.InventoryItemId, out InventoryItem? item))
                    continue;
                if (
                    !maxSeverityByPackage.TryGetValue(item.Name, out Severity existing)
                    || finding.Severity > existing
                )
                {
                    maxSeverityByPackage[item.Name] = finding.Severity;
                }
            }

            entries = entries
                .OrderByDescending(entry =>
                    maxSeverityByPackage.TryGetValue(entry.PackageName, out Severity severity)
                        ? (int)severity
                        : 0
                )
                .Take(maxPackages.Value)
                .ToList();
        }

        if (dryRun || entries.Count == 0)
        {
            return new(
                DryRun: dryRun,
                PullRequestUrl: null,
                Entries: entries,
                Errors: errors,
                ReusedBranch: null,
                MajorBumps: majorBumps,
                Warnings: warnings
            );
        }

        // Apply each manifest edit into a temp working tree, fetching the original from the
        // source on first touch. IRepoSourceFs handles the GitHub Contents API call; the
        // ecosystem's manifest editor reads + writes the file in workRoot.
        string workRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-bulk-apply",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(workRoot);

        try
        {
            HashSet<string> touchedManifests = [];
            HashSet<string> fetchedPaths = [];

            foreach (BulkApplyEntry entry in entries)
            {
                IEcosystem? ecoForApply = _ecosystems.For(entry.Ecosystem);
                if (ecoForApply is null)
                    continue;

                if (fetchedPaths.Add(entry.ManifestPath))
                {
                    string? remoteContent = await _sourceFs.ReadFileAsync(
                        source,
                        entry.ManifestPath,
                        ct
                    );
                    if (remoteContent is null)
                    {
                        errors.Add(
                            new(
                                entry.PackageName,
                                $"Manifest '{entry.ManifestPath}' not found in repo."
                            )
                        );
                        continue;
                    }
                    string localPath = Path.Combine(
                        workRoot,
                        entry.ManifestPath.Replace('/', Path.DirectorySeparatorChar)
                    );
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    await File.WriteAllTextAsync(localPath, remoteContent, ct);
                }

                InventoryItem dummyItem = BuildInventoryItem(entry, byPackage, itemsById);
                ManifestEditOutcome outcome = ecoForApply.Apply(
                    workRoot,
                    dummyItem,
                    entry.SuggestedVersion
                );

                if (outcome.UnsupportedReason is not null)
                {
                    errors.Add(new(entry.PackageName, outcome.UnsupportedReason));
                    continue;
                }

                foreach (string changed in outcome.ChangedFiles)
                {
                    string relative = Path.GetRelativePath(workRoot, changed).Replace('\\', '/');
                    touchedManifests.Add(relative);
                }
            }

            // JSON parse verification — abort the whole apply if any manifest is invalid.
            foreach (string relative in touchedManifests)
            {
                string localPath = Path.Combine(
                    workRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)
                );
                if (!File.Exists(localPath))
                    continue;
                try
                {
                    JsonDocument.Parse(File.ReadAllText(localPath)).Dispose();
                }
                catch (JsonException)
                {
                    return new(
                        DryRun: false,
                        PullRequestUrl: null,
                        Entries: entries,
                        Errors:
                        [
                            .. errors,
                            new(
                                relative,
                                $"Edited {relative} no longer parses as valid JSON. Apply aborted."
                            ),
                        ],
                        ReusedBranch: null,
                        MajorBumps: majorBumps,
                        Warnings: warnings
                    );
                }
            }

            // Hand the final file contents to the shared PR opener — it owns blob/tree/commit/
            // branch/PR creation across both BulkFix (advisory-driven) and Updates apply paths.
            List<RepoFileEdit> edits = [];
            foreach (string relative in touchedManifests)
            {
                string localPath = Path.Combine(
                    workRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)
                );
                if (!File.Exists(localPath))
                    continue;
                edits.Add(new(relative, await File.ReadAllTextAsync(localPath, ct)));
            }

            if (edits.Count == 0)
            {
                return new(
                    DryRun: false,
                    PullRequestUrl: null,
                    Entries: entries,
                    Errors: [.. errors, new("(manifest)", "No manifest files were modified.")],
                    ReusedBranch: null,
                    MajorBumps: majorBumps,
                    Warnings: warnings
                );
            }

            RepoPullRequestResult prResult = await _prOpener.OpenAsync(
                source,
                edits,
                new(
                    BranchPrefix: "shield/auto-fix",
                    CommitMessage: BuildCommitMessage(entries),
                    PrTitle: BuildPrTitle(entries),
                    PrBody: BuildPrBody(entries, majorBumps)
                ),
                ct
            );

            return new(
                DryRun: false,
                PullRequestUrl: prResult.PullRequestUrl,
                Entries: entries,
                Errors:
                [
                    .. errors,
                    .. prResult.Errors.Select(error => new BulkApplyError(
                        $"({error.Source})",
                        error.Message
                    )),
                ],
                ReusedBranch: prResult.BranchName,
                MajorBumps: majorBumps,
                Warnings: warnings
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

    private static string BuildPrTitle(IReadOnlyList<BulkApplyEntry> entries)
    {
        int advisoryCount = entries.Sum(entry => entry.AdvisoryIds.Count);
        return $"chore(deps): bulk security fix — {entries.Count} packages, {advisoryCount} advisories";
    }

    private static string BuildCommitMessage(IReadOnlyList<BulkApplyEntry> entries)
    {
        return BuildPrTitle(entries);
    }

    private string BuildPrBody(
        IReadOnlyList<BulkApplyEntry> entries,
        IReadOnlyList<BulkApplyEntry> majorBumps
    )
    {
        StringBuilder body = new();
        body.AppendLine("Automated bulk security fix by Shield.\n");

        IEnumerable<IGrouping<string, BulkApplyEntry>> byManifest = entries.GroupBy(
            entry => entry.ManifestPath,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (IGrouping<string, BulkApplyEntry> group in byManifest)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"**{group.Key}**\n");
            foreach (BulkApplyEntry entry in group)
            {
                string advisoryList = FormatAdvisoryIds(entry.AdvisoryIds);
                string changelogLink =
                    _ecosystems
                        .For(entry.Ecosystem)
                        ?.ChangelogUrl(entry.PackageName, entry.SuggestedVersion)
                    ?? entry.PackageName;
                body.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"- `{entry.PackageName}` [`{entry.CurrentVersion}` → [{entry.SuggestedVersion}]({changelogLink})] — covers {entry.AdvisoryIds.Count} {(entry.AdvisoryIds.Count == 1 ? "advisory" : "advisories")} ({advisoryList})"
                );
            }
            body.AppendLine();
        }

        if (majorBumps.Count > 0)
        {
            body.AppendLine("---");
            body.AppendLine(
                CultureInfo.InvariantCulture,
                $"⚠️ **{majorBumps.Count} major-version {(majorBumps.Count == 1 ? "bump was" : "bumps were")} skipped** (re-submit with `allowMajorBumps: true` to include):\n"
            );
            foreach (BulkApplyEntry entry in majorBumps)
            {
                body.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"- `{entry.PackageName}` `{entry.CurrentVersion}` → `{entry.SuggestedVersion}` (major)"
                );
            }
            body.AppendLine();
        }

        body.AppendLine("---");
        body.AppendLine(
            "Suggested by [Shield](https://github.com/nomercylabs/shield). Review before merging."
        );
        return body.ToString().TrimEnd();
    }

    private static string FormatAdvisoryIds(IReadOnlyList<string> ids)
    {
        const int maxInline = 3;
        if (ids.Count <= maxInline)
            return string.Join(", ", ids);
        return string.Join(", ", ids.Take(maxInline)) + $", …and {ids.Count - maxInline} more";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 60)
            return $"{(int)age.TotalMinutes}m";
        if (age.TotalHours < 24)
            return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}d";
    }

    private static InventoryItem BuildInventoryItem(
        BulkApplyEntry entry,
        Dictionary<(Ecosystem, string), List<Finding>> byPackage,
        Dictionary<int, InventoryItem> itemsById
    )
    {
        foreach (KeyValuePair<(Ecosystem Eco, string Name), List<Finding>> kv in byPackage)
        {
            if (!string.Equals(kv.Key.Name, entry.PackageName, StringComparison.Ordinal))
                continue;
            if (kv.Value.Count == 0)
                continue;
            if (itemsById.TryGetValue(kv.Value[0].InventoryItemId, out InventoryItem? real))
                return real;
        }

        return new()
        {
            Ecosystem = Ecosystem.Npm,
            Name = entry.PackageName,
            Version = entry.CurrentVersion,
            IsDirect = true,
            ManifestPath = entry.ManifestPath,
        };
    }
}
