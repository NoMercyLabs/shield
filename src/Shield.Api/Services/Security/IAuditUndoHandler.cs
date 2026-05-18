namespace Shield.Api.Services.Security;

// Per-action handler that knows how to invert a single Action string. SourceUpdateUndoHandler
// reads BeforeJson as a Source-shaped record + applies it; RoleChangeUndoHandler reads a
// role list + reassigns it; etc. Registered via DI keyed by Action.
public interface IAuditUndoHandler
{
    string Action { get; }

    // Applies the inverse of the captured action. Returns a short human-readable summary
    // that lands in the new audit entry's DetailsJson so the timeline shows what flipped.
    // Throws when BeforeJson can't be deserialized — caller surfaces as 400.
    Task<AuditUndoResult> UndoAsync(AuditEntry entry, CancellationToken ct);
}

public sealed record AuditUndoResult(bool Success, string Summary, object? Details = null);

// Resolves the handler for a given Action. DI-backed; unknown actions return null so the
// /api/audit/{id}/undo endpoint can return 400 "not reversible" instead of a registration
// error.
public interface IAuditUndoRegistry
{
    IAuditUndoHandler? For(string action);
}

public sealed class AuditUndoRegistry : IAuditUndoRegistry
{
    private readonly Dictionary<string, IAuditUndoHandler> _byAction;

    public AuditUndoRegistry(IEnumerable<IAuditUndoHandler> handlers)
    {
        _byAction = handlers.ToDictionary(handler => handler.Action, StringComparer.Ordinal);
    }

    public IAuditUndoHandler? For(string action) =>
        _byAction.TryGetValue(action, out IAuditUndoHandler? handler) ? handler : null;
}
