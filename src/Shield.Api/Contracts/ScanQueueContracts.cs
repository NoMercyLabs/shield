namespace Shield.Api.Contracts;

public sealed record ScanQueuedResponse(
    bool Accepted,
    DateTime QueuedAt,
    DateTime? EstimatedCompletion
);

// "Pick from GitHub" bulk-add — one selection per repo the operator ticked. Branch is
// optional; when null the scanner falls back to the repo's default branch.
public sealed record BulkSelection(string Owner, string Name, string? Branch);

public sealed record BulkFromGithubRequest(
    IReadOnlyList<BulkSelection> Selections,
    TimeSpan? DefaultScanInterval = null
);

public sealed record BulkFromGithubResponse(
    int Created,
    int SkippedExisting,
    IReadOnlyList<SourceResponse> Sources
);

public sealed record ScanQueueFailureItem(
    Guid Id,
    int SourceId,
    DateTime EnqueuedAt,
    DateTime CompletedAt,
    int Attempts,
    string ErrorMessage
);

public sealed record ScanQueueStatusResponse(
    int Pending,
    int InProgress,
    IReadOnlyList<ScanQueueFailureItem> RecentFailures
);
