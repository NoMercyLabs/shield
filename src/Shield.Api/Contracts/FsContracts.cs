namespace Shield.Api.Contracts;

public sealed record FsEntry(
    string Name,
    string Path,
    bool IsDirectory,
    bool HasLockfiles,
    bool HasGitRepo,
    int? LockfileCount,
    long? Size
);

public sealed record FsBrowseResponse(
    string Path,
    string? Parent,
    IReadOnlyList<FsEntry> Entries,
    string[] Roots,
    bool HasLockfiles
);

public sealed record BulkLocalFoldersRequest(
    IReadOnlyList<string> Paths,
    string DefaultScanInterval = "06:00:00"
);

public sealed record BulkLocalFoldersResponse(
    int Created,
    int SkippedExisting,
    IReadOnlyList<SourceResponse> Sources
);
