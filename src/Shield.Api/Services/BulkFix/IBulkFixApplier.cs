namespace Shield.Api.Services.BulkFix;

public interface IBulkFixApplier
{
    Task<BulkApplyResult> ApplyAllPullRequestAsync(
        Source source,
        IReadOnlyList<Advisory> allAdvisories,
        bool dryRun,
        int? maxPackages,
        bool allowMajorBumps,
        CancellationToken ct
    );
}
