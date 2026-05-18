namespace Shield.Api.Services;

// Helpers for the standardised "side-channel call failed, swallow + log at Debug" pattern.
// Used by controllers that fire audit / notification / security-event calls after the primary
// action — if those side-channels fail, the user-visible response should still succeed.
public static class LoggerExtensions
{
    public static void LogBestEffortFailure(this ILogger logger, Exception ex, string context) =>
        logger.LogDebug(ex, "Best-effort side-channel failed: {Context}", context);

    public static void LogBestEffortFailure(this ILogger logger, Exception ex) =>
        logger.LogDebug(ex, "Best-effort side-channel failed");
}
