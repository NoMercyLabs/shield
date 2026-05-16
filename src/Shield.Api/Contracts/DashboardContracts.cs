using Shield.Core.Domain;

namespace Shield.Api.Contracts;

public sealed record OpenCounts(int Low, int Medium, int High, int Critical);

public sealed record DashboardResponse(
    OpenCounts OpenCounts,
    int SourcesHealthy,
    int SourcesStale,
    IReadOnlyList<FindingResponse> RecentFindings
);
