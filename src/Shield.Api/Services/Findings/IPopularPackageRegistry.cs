namespace Shield.Api.Services.Findings;

// Per-ecosystem curated lists of well-known package names. Used by the typosquat
// detector — a candidate within Levenshtein distance 2 of one of these names is
// suspicious. Empty set means "no curation yet for this ecosystem", treated as
// "skip the typosquat check" rather than as "nothing is popular".
public interface IPopularPackageRegistry
{
    IReadOnlySet<string> For(Ecosystem ecosystem);
}
