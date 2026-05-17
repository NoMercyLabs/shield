using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Octokit;
using Shield.Api.Services.ManifestEditors;
using Shield.Core.Abstractions;
using Shield.Core.Domain;
using Shield.Data;
using Shield.Scanners;

namespace Shield.Api.Services;

public sealed record BulkApplyEntry(
    string PackageName,
    string CurrentVersion,
    string SuggestedVersion,
    string ManifestPath,
    IReadOnlyList<string> AdvisoryIds
);

public sealed record BulkApplyError(string PackageName, string Reason);

public sealed record BulkApplyResult(
    bool DryRun,
    string? PullRequestUrl,
    IReadOnlyList<BulkApplyEntry> Entries,
    IReadOnlyList<BulkApplyError> Errors,
    string? ReusedBranch
);

public interface IBulkFixApplier
{
    Task<BulkApplyResult> ApplyAllPullRequestAsync(
        Source source,
        IReadOnlyList<Advisory> allAdvisories,
        bool dryRun,
        int? maxPackages,
        CancellationToken ct
    );
}

public sealed class BulkFixApplier : IBulkFixApplier
{
    private static readonly HashSet<Ecosystem> PrSupportedEcosystems =
    [
        Ecosystem.Npm,
        Ecosystem.Composer,
    ];
    private static readonly ProductHeaderValue ProductHeader = new("Shield");

    private readonly ShieldDbContext _db;
    private readonly FeedsDbContext _feedsDb;
    private readonly IFixSuggester _suggester;
    private readonly Dictionary<Ecosystem, IManifestEditor> _editors;
    private readonly IOAuthTokenAccessor _tokenAccessor;

    public BulkFixApplier(
        ShieldDbContext db,
        FeedsDbContext feedsDb,
        IFixSuggester suggester,
        IEnumerable<IManifestEditor> editors,
        IOAuthTokenAccessor tokenAccessor
    )
    {
        _db = db;
        _feedsDb = feedsDb;
        _suggester = suggester;
        _editors = editors.ToDictionary(editor => editor.Ecosystem);
        _tokenAccessor = tokenAccessor;
    }

    public async Task<BulkApplyResult> ApplyAllPullRequestAsync(
        Source source,
        IReadOnlyList<Advisory> allAdvisories,
        bool dryRun,
        int? maxPackages,
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
                ReusedBranch: null
            );
        }

        GitHubRepoConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GitHubRepoConfig>(source.ConfigJson);
        }
        catch (JsonException ex)
        {
            return new(
                DryRun: dryRun,
                PullRequestUrl: null,
                Entries: [],
                Errors: [new("(source)", $"Invalid GithubRepo config: {ex.Message}")],
                ReusedBranch: null
            );
        }

        if (
            config is null
            || string.IsNullOrWhiteSpace(config.Owner)
            || string.IsNullOrWhiteSpace(config.Repo)
        )
        {
            return new(
                DryRun: dryRun,
                PullRequestUrl: null,
                Entries: [],
                Errors: [new("(source)", "GithubRepo config missing 'owner' or 'repo'.")],
                ReusedBranch: null
            );
        }

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
                ReusedBranch: null
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
        List<BulkApplyError> errors = [];

        foreach (KeyValuePair<(Ecosystem Eco, string Name), List<Finding>> kv in byPackage)
        {
            (Ecosystem eco, string packageName) = kv.Key;

            if (!PrSupportedEcosystems.Contains(eco))
            {
                errors.Add(new(packageName, $"Ecosystem {eco} doesn't support automated PRs."));
                continue;
            }

            if (!_editors.ContainsKey(eco))
            {
                errors.Add(new(packageName, $"No manifest editor for ecosystem {eco}."));
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
                : DefaultManifestFor(eco);

            List<string> coveredAdvisoryIds = packageAdvisories
                .Select(advisory => advisory.ExternalId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            entries.Add(
                new(
                    PackageName: packageName,
                    CurrentVersion: representativeItem.Version,
                    SuggestedVersion: suggestion.SuggestedVersion,
                    ManifestPath: manifestPath,
                    AdvisoryIds: coveredAdvisoryIds
                )
            );
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
                ReusedBranch: null
            );
        }

        // Real apply — need GitHub token.
        string? token = await _tokenAccessor.GetAccessTokenAsync(OAuthProvider.Github, ct);
        if (string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(config.Token))
            token = config.Token;
        if (string.IsNullOrEmpty(token))
        {
            return new(
                DryRun: false,
                PullRequestUrl: null,
                Entries: entries,
                Errors:
                [
                    .. errors,
                    new(
                        "(auth)",
                        "No GitHub OAuth token available. Connect GitHub in Settings or provide a per-source PAT."
                    ),
                ],
                ReusedBranch: null
            );
        }

        GitHubClient client = new(ProductHeader) { Credentials = new(token) };

        Repository repo;
        try
        {
            repo = await client.Repository.Get(config.Owner, config.Repo);
        }
        catch (Exception ex)
        {
            return new(
                DryRun: false,
                PullRequestUrl: null,
                Entries: entries,
                Errors: [.. errors, new("(repo)", $"Failed to fetch repo: {ex.Message}")],
                ReusedBranch: null
            );
        }

        string defaultBranch = string.IsNullOrWhiteSpace(config.Branch)
            ? repo.DefaultBranch
            : config.Branch!;

        Reference baseRef;
        try
        {
            baseRef = await client.Git.Reference.Get(repo.Id, $"heads/{defaultBranch}");
        }
        catch (Exception ex)
        {
            return new(
                DryRun: false,
                PullRequestUrl: null,
                Entries: entries,
                Errors:
                [
                    .. errors,
                    new("(branch)", $"Failed to fetch base branch '{defaultBranch}': {ex.Message}"),
                ],
                ReusedBranch: null
            );
        }

        // Apply all manifest edits in a temp dir, collect changed file blobs.
        string workRoot = Path.Combine(
            Path.GetTempPath(),
            "shield-bulk-apply",
            Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(workRoot);

        try
        {
            List<NewTreeItem> treeItems = [];
            HashSet<string> processedManifests = [];

            foreach (BulkApplyEntry entry in entries)
            {
                string manifestRelative = entry.ManifestPath;

                if (!processedManifests.Contains(manifestRelative))
                {
                    // Fetch the manifest file content from GitHub if we haven't already.
                    string localPath = Path.Combine(
                        workRoot,
                        manifestRelative.Replace('/', Path.DirectorySeparatorChar)
                    );
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                    if (!File.Exists(localPath))
                    {
                        try
                        {
                            IReadOnlyList<RepositoryContent> contents =
                                await client.Repository.Content.GetAllContentsByRef(
                                    repo.Id,
                                    manifestRelative,
                                    defaultBranch
                                );
                            if (contents.Count > 0)
                                File.WriteAllText(localPath, contents[0].Content);
                        }
                        catch (NotFoundException)
                        {
                            errors.Add(
                                new(
                                    entry.PackageName,
                                    $"Manifest '{manifestRelative}' not found in repo."
                                )
                            );
                            continue;
                        }
                    }
                }

                // Apply the bump in the working copy.
                if (
                    !_editors.TryGetValue(
                        EcosystemForPackage(entry, byPackage, itemsById),
                        out IManifestEditor? editor
                    )
                )
                    continue;

                InventoryItem dummyItem = BuildInventoryItem(entry, byPackage, itemsById);
                ManifestEditOutcome outcome = editor.Apply(
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
                    if (!processedManifests.Add(relative))
                        continue;

                    string content = File.ReadAllText(changed);
                    BlobReference blob = await client.Git.Blob.Create(
                        repo.Id,
                        new() { Content = content, Encoding = EncodingType.Utf8 }
                    );
                    treeItems.Add(
                        new()
                        {
                            Path = relative,
                            Mode = "100644",
                            Type = TreeType.Blob,
                            Sha = blob.Sha,
                        }
                    );
                }
            }

            if (treeItems.Count == 0)
            {
                return new(
                    DryRun: false,
                    PullRequestUrl: null,
                    Entries: entries,
                    Errors: [.. errors, new("(manifest)", "No manifest files were modified.")],
                    ReusedBranch: null
                );
            }

            NewTree newTree = new() { BaseTree = baseRef.Object.Sha };
            foreach (NewTreeItem treeItem in treeItems)
                newTree.Tree.Add(treeItem);
            TreeResponse tree = await client.Git.Tree.Create(repo.Id, newTree);

            string commitMessage = BuildCommitMessage(entries);
            NewCommit newCommit = new(commitMessage, tree.Sha, baseRef.Object.Sha);
            Commit commit = await client.Git.Commit.Create(repo.Id, newCommit);

            string today = DateTime.UtcNow.ToString("yyyyMMdd");
            string branchName = $"shield/auto-fix-{today}";
            string? reusedBranch = null;

            // Idempotency: try to find existing branch; fast-forward if content differs.
            Reference? existingRef = null;
            try
            {
                existingRef = await client.Git.Reference.Get(repo.Id, $"heads/{branchName}");
            }
            catch (NotFoundException)
            {
                existingRef = null;
            }

            if (existingRef is not null)
            {
                reusedBranch = branchName;
                await client.Git.Reference.Update(
                    repo.Id,
                    $"heads/{branchName}",
                    new(commit.Sha, force: true)
                );
            }
            else
            {
                try
                {
                    await client.Git.Reference.Create(
                        repo.Id,
                        new($"refs/heads/{branchName}", commit.Sha)
                    );
                }
                catch (ApiValidationException)
                {
                    // Race — another process beat us; fall back to counter suffix.
                    branchName = $"shield/auto-fix-{today}-{Guid.NewGuid().ToString("n")[..6]}";
                    await client.Git.Reference.Create(
                        repo.Id,
                        new($"refs/heads/{branchName}", commit.Sha)
                    );
                }
            }

            string prBody = BuildPrBody(entries);
            string prTitle = BuildPrTitle(entries);

            // Check for existing open PR from this branch before creating one.
            string? pullRequestUrl = null;
            if (reusedBranch is not null)
            {
                IReadOnlyList<PullRequest> existing = await client.PullRequest.GetAllForRepository(
                    repo.Id,
                    new PullRequestRequest
                    {
                        Head = $"{config.Owner}:{branchName}",
                        State = ItemStateFilter.Open,
                    }
                );
                if (existing.Count > 0)
                {
                    PullRequest existingPr = await client.PullRequest.Update(
                        repo.Id,
                        existing[0].Number,
                        new PullRequestUpdate { Body = prBody, Title = prTitle }
                    );
                    pullRequestUrl = existingPr.HtmlUrl;
                }
            }

            if (pullRequestUrl is null)
            {
                PullRequest pr = await client.PullRequest.Create(
                    repo.Id,
                    new(prTitle, branchName, defaultBranch) { Body = prBody }
                );
                pullRequestUrl = pr.HtmlUrl;
            }

            return new(
                DryRun: false,
                PullRequestUrl: pullRequestUrl,
                Entries: entries,
                Errors: errors,
                ReusedBranch: reusedBranch
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

    private static string BuildPrBody(IReadOnlyList<BulkApplyEntry> entries)
    {
        StringBuilder body = new();
        body.AppendLine("Automated bulk security fix by Shield.\n");

        IEnumerable<IGrouping<string, BulkApplyEntry>> byManifest = entries.GroupBy(
            entry => entry.ManifestPath,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (IGrouping<string, BulkApplyEntry> group in byManifest)
        {
            body.AppendLine($"**{group.Key}**\n");
            foreach (BulkApplyEntry entry in group)
            {
                string advisoryList = FormatAdvisoryIds(entry.AdvisoryIds);
                body.AppendLine(
                    $"- `{entry.PackageName}`: `{entry.CurrentVersion}` → `{entry.SuggestedVersion}` ({advisoryList})"
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

    private static string DefaultManifestFor(Ecosystem ecosystem) =>
        ecosystem switch
        {
            Ecosystem.Npm => "package.json",
            Ecosystem.Composer => "composer.json",
            _ => "package.json",
        };

    private static Ecosystem EcosystemForPackage(
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
            if (itemsById.TryGetValue(kv.Value[0].InventoryItemId, out InventoryItem? item))
                return item.Ecosystem;
        }
        return Ecosystem.Npm;
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
