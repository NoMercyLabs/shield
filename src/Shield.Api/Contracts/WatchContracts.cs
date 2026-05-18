using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record PackageWatchResponse(
    Guid Id,
    Ecosystem Ecosystem,
    string PackageName,
    DateTime AddedAt
)
{
    public static PackageWatchResponse From(PackageWatch watch) =>
        new(watch.Id, watch.Ecosystem, watch.PackageName, watch.AddedAt);
}

public sealed record CreateWatchRequest(Ecosystem Ecosystem, string PackageName);

public sealed record WatchSummaryRow(
    Ecosystem Ecosystem,
    string PackageName,
    int SourceCount,
    WatchOpenCounts OpenFindings
);

public sealed record WatchOpenCounts(int Low, int Medium, int High, int Critical);
