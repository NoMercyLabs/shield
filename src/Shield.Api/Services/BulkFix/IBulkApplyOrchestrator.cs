using Shield.Api.Contracts;
using Shield.Core.Domain;

namespace Shield.Api.Services.BulkFix;

// Orchestrates the advisory-driven bulk-apply path. Owns the validation gates (source type,
// production confirmation, manual cooldown), the advisory lookup, the call into IBulkFixApplier,
// and the post-success side-channels (audit, notifications, security event). The controller
// becomes a thin endpoint that just dispatches to ApplyAsync and maps the result to HTTP.
public interface IBulkApplyOrchestrator
{
    Task<BulkApplyDispatchResult> ApplyAsync(
        int sourceId,
        BulkApplyRequest request,
        CancellationToken ct
    );
}

public sealed record BulkApplyDispatchResult(
    BulkApplyOutcome Outcome,
    BulkApplyResponse? Response = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    DateTime? RetryAfter = null
);

public enum BulkApplyOutcome
{
    Ok = 0,
    SourceNotFound = 1,
    UnsupportedType = 2,
    ProductionConfirmationRequired = 3,
    Cooldown = 4,
}
