using Shield.Api.Services.ManifestEditors;

namespace Shield.Api.Services.Ecosystems;

public sealed record LatestPackageInfo(string Version, DateTime? PublishedAt);

// Single contract for every package ecosystem Shield supports. Each implementation owns ALL
// per-language behaviour — latest-version probe, manifest editing, registry URLs, anomaly
// data, capability flags. Adding a new ecosystem = one new file implementing this interface +
// one DI registration. No central dispatcher switch, no ecosystem-keyed dictionaries scattered
// across services.
public interface IEcosystem
{
    Ecosystem Ecosystem { get; }

    // The conventional manifest filename used at a source's root when one isn't otherwise
    // discoverable. Editors that touch multiple manifests (monorepos) override this with
    // per-item ManifestPath, but consumers need a fallback when nothing's on file yet.
    string DefaultManifestPath { get; }

    // True when this ecosystem has an IEcosystem.Apply implementation that can patch a
    // manifest on disk + emit a PR. False for ecosystems we only probe today (Maven, Pub, etc).
    bool SupportsAutomaticPullRequests { get; }

    // Display links for the SPA / PR body. PackageUrl is the package page; ChangelogUrl points
    // at a specific version where possible.
    string PackageUrl(string packageName);
    string ChangelogUrl(string packageName, string version);

    // Latest-stable lookup against the ecosystem's native registry. Null on not-found / probe
    // failure — callers treat null as "no update detected" rather than surfacing an error.
    Task<LatestPackageInfo?> GetLatestStableAsync(string packageName, CancellationToken ct);

    // Manifest edit. `rootPath` is the working copy root (LocalFolder source path or temp
    // clone for GithubRepo apply); the editor enumerates candidate manifests and patches in
    // place. Returns ChangedFiles + optional follow-up command (e.g. `npm install`).
    ManifestEditOutcome Apply(string rootPath, InventoryItem item, string suggestedVersion);
}
