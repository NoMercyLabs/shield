using Shield.Scanners;

namespace Shield.Api.Services.FixApply;

public interface IFixApplier
{
    Task<ApplyFixResult> ApplyLocalAsync(
        Source source,
        InventoryItem item,
        FixSuggestion suggestion,
        CancellationToken ct
    );

    Task<ApplyFixResult> ApplyPullRequestAsync(
        Source source,
        InventoryItem item,
        Advisory advisory,
        FixSuggestion suggestion,
        CancellationToken ct
    );
}
