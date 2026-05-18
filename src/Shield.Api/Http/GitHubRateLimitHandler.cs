using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Shield.Api.Http;

// Singleton bag of per-principal rate-limit knowledge. Lives outside the DelegatingHandler
// so the handler can be re-instantiated for Octokit's per-client HttpMessageHandler chain
// without losing the cached cap state.
public sealed class GitHubRateLimitStore
{
    private readonly ConcurrentDictionary<string, GitHubRateLimitHandler.RateLimitState> _state =
        new(StringComparer.Ordinal);

    public bool TryGet(string key, out GitHubRateLimitHandler.RateLimitState state) =>
        _state.TryGetValue(key, out state!);

    public void Update(string key, GitHubRateLimitHandler.RateLimitState state) =>
        _state.AddOrUpdate(key, state, (_, _) => state);

    public IReadOnlyDictionary<string, GitHubRateLimitHandler.RateLimitSnapshot> Snapshot() =>
        _state.ToDictionary(
            entry => entry.Key,
            entry => new GitHubRateLimitHandler.RateLimitSnapshot(
                entry.Value.Remaining,
                entry.Value.Limit,
                entry.Value.ResetAt,
                entry.Value.LastUpdated
            )
        );
}

// Delegating handler that keeps every outbound GitHub call honest about rate limits.
// Three policies layered together: primary (X-RateLimit-* — sleep proactively when the
// per-token bucket is near empty), secondary (429 / 403 + Retry-After — single retry on
// abuse-detection), and transient 5xx (exponential backoff, 3 attempts).
//
// State is keyed per principal (hash of bearer token, falling back to the GitHub App
// installation header, falling back to "anon"). The shared GitHubRateLimitStore singleton
// holds the per-key cap so HttpClient churn doesn't reset our awareness of the bucket.
// Sleeps are bounded at 1h so a clock-skewed Reset header can't park a worker forever.
public sealed class GitHubRateLimitHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromHours(1);
    private const int RemainingThreshold = 50;
    private const int Max5XxAttempts = 3;

    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    private readonly GitHubRateLimitStore _store;
    private readonly ILogger<GitHubRateLimitHandler> _log;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GitHubRateLimitHandler(GitHubRateLimitStore store, ILogger<GitHubRateLimitHandler> log)
        : this(store, log, TimeProvider.System, Task.Delay) { }

    // Test seam: inject a fake clock + a no-op (or fast) delay so the WireMock tests don't
    // really sleep a second per attempt.
    internal GitHubRateLimitHandler(
        GitHubRateLimitStore store,
        ILogger<GitHubRateLimitHandler> log,
        TimeProvider time,
        Func<TimeSpan, CancellationToken, Task> delay
    )
    {
        _store = store;
        _log = log;
        _time = time;
        _delay = delay;
    }

    // Exposed for diagnostics (the /api/scan-queue endpoint surfaces the current cap state
    // so operators can see why a worker is parked).
    public IReadOnlyDictionary<string, RateLimitSnapshot> Snapshot() => _store.Snapshot();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        string key = BuildPrincipalKey(request);

        // Primary: if the cached bucket says we're below the threshold, park before we even
        // hit the wire. Re-check after sleep in case another caller refreshed the value.
        await WaitIfBucketNearEmptyAsync(key, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response = await SendWithRetriesAsync(request, key, cancellationToken)
            .ConfigureAwait(false);
        UpdateState(key, response);
        return response;
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        HttpRequestMessage request,
        string principalKey,
        CancellationToken ct
    )
    {
        HttpResponseMessage? response = null;
        bool secondaryRetried = false;
        bool primaryRetried = false;

        for (int attempt = 0; attempt < Max5XxAttempts; attempt++)
        {
            // HttpRequestMessage is single-use once sent. On retry we shallow-clone the request
            // (headers + content references — content is null for GETs which is all the GitHub
            // tree/blob reads we care about here; for the rare POST we still copy the buffer).
            HttpRequestMessage outbound = attempt == 0 ? request : await CloneAsync(request, ct);
            response = await base.SendAsync(outbound, ct).ConfigureAwait(false);

            // Always learn from the headers we just got back.
            UpdateState(principalKey, response);

            // Secondary rate-limit: 429 always, or 403 carrying Retry-After. One retry only —
            // GitHub explicitly says repeated abuse-detection hits will permanently throttle.
            if (
                !secondaryRetried
                && (
                    response.StatusCode == HttpStatusCode.TooManyRequests
                    || (
                        response.StatusCode == HttpStatusCode.Forbidden
                        && response.Headers.RetryAfter is not null
                    )
                )
            )
            {
                TimeSpan wait = ExtractRetryAfter(response) ?? TimeSpan.FromSeconds(60);
                wait = Clamp(wait);
                _log.LogWarning(
                    "GitHub secondary rate limit hit (key {Key}, status {Status}); sleeping {Wait}s before retry",
                    principalKey,
                    (int)response.StatusCode,
                    wait.TotalSeconds
                );
                response.Dispose();
                await _delay(wait, ct).ConfigureAwait(false);
                secondaryRetried = true;
                continue;
            }

            // Primary rate-limit exhausted (403 with X-RateLimit-Remaining: 0). Sleep until reset.
            if (
                !primaryRetried
                && response.StatusCode == HttpStatusCode.Forbidden
                && TryGetHeaderInt(response, "X-RateLimit-Remaining", out int remaining)
                && remaining == 0
            )
            {
                TimeSpan wait = ComputeSleepUntilReset(response) ?? TimeSpan.FromMinutes(1);
                wait = Clamp(wait);
                _log.LogWarning(
                    "GitHub primary rate limit exhausted (key {Key}); sleeping {Wait}s until reset",
                    principalKey,
                    wait.TotalSeconds
                );
                response.Dispose();
                await _delay(wait, ct).ConfigureAwait(false);
                primaryRetried = true;
                continue;
            }

            // 5xx (except 503 with Retry-After, which we honour like a secondary limit).
            int code = (int)response.StatusCode;
            if (code is >= 500 and < 600)
            {
                if (
                    response.StatusCode == HttpStatusCode.ServiceUnavailable
                    && response.Headers.RetryAfter is not null
                    && !secondaryRetried
                )
                {
                    TimeSpan wait = Clamp(ExtractRetryAfter(response) ?? TimeSpan.FromSeconds(30));
                    response.Dispose();
                    await _delay(wait, ct).ConfigureAwait(false);
                    secondaryRetried = true;
                    continue;
                }

                if (attempt + 1 < Max5XxAttempts)
                {
                    TimeSpan backoff = BackoffSchedule[attempt];
                    _log.LogWarning(
                        "GitHub {Status} (attempt {Attempt}/{Total}); backing off {Backoff}s",
                        code,
                        attempt + 1,
                        Max5XxAttempts,
                        backoff.TotalSeconds
                    );
                    response.Dispose();
                    await _delay(backoff, ct).ConfigureAwait(false);
                    continue;
                }
            }

            return response;
        }

        // All retries exhausted — return the last response so the caller sees the real status.
        return response!;
    }

    private async Task WaitIfBucketNearEmptyAsync(string key, CancellationToken ct)
    {
        if (!_store.TryGet(key, out RateLimitState state))
            return;
        if (state.Remaining >= RemainingThreshold)
            return;
        if (state.ResetAt is null)
            return;

        TimeSpan wait = state.ResetAt.Value - _time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
            return;

        TimeSpan capped = Clamp(wait);
        _log.LogWarning(
            "GitHub bucket below threshold (key {Key}, remaining {Remaining}/{Limit}); sleeping {Wait}s",
            key,
            state.Remaining,
            state.Limit,
            capped.TotalSeconds
        );
        await _delay(capped, ct).ConfigureAwait(false);
    }

    private void UpdateState(string key, HttpResponseMessage response)
    {
        if (!TryGetHeaderInt(response, "X-RateLimit-Remaining", out int remaining))
            return;
        TryGetHeaderInt(response, "X-RateLimit-Limit", out int limit);

        DateTimeOffset? resetAt = null;
        if (
            response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? resetValues)
        )
        {
            string? raw = resetValues.FirstOrDefault();
            if (
                long.TryParse(
                    raw,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long unix
                )
            )
                resetAt = DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        RateLimitState next = new(remaining, limit, resetAt, _time.GetUtcNow());
        _store.Update(key, next);
    }

    private static TimeSpan? ExtractRetryAfter(HttpResponseMessage response)
    {
        System.Net.Http.Headers.RetryConditionHeaderValue? header = response.Headers.RetryAfter;
        if (header is null)
            return null;
        if (header.Delta is { } delta)
            return delta;
        if (header.Date is { } when)
            return when - DateTimeOffset.UtcNow;
        return null;
    }

    private TimeSpan? ComputeSleepUntilReset(HttpResponseMessage response)
    {
        if (
            !response.Headers.TryGetValues(
                "X-RateLimit-Reset",
                out IEnumerable<string>? resetValues
            )
        )
            return null;
        if (
            !long.TryParse(
                resetValues.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long unix
            )
        )
            return null;
        DateTimeOffset resetAt = DateTimeOffset.FromUnixTimeSeconds(unix);
        TimeSpan delta = resetAt - _time.GetUtcNow();
        return delta > TimeSpan.Zero ? delta : TimeSpan.FromSeconds(1);
    }

    private static TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;
        return value > MaxSleep ? MaxSleep : value;
    }

    private static bool TryGetHeaderInt(HttpResponseMessage response, string name, out int value)
    {
        value = 0;
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            return false;
        return int.TryParse(
            values.FirstOrDefault(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private static string BuildPrincipalKey(HttpRequestMessage request)
    {
        string? raw = null;
        if (request.Headers.Authorization?.Parameter is { } bearer && bearer.Length > 0)
            raw = "bearer:" + bearer;
        else if (
            request.Headers.TryGetValues("installation", out IEnumerable<string>? installValues)
        )
            raw = "installation:" + installValues.FirstOrDefault();

        if (string.IsNullOrEmpty(raw))
            return "anon";

        // Don't store the plaintext bearer as a dictionary key — hash it so a heap dump or
        // diagnostic dump of the singleton doesn't leak the token.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash, 0, 8);
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage original,
        CancellationToken ct
    )
    {
        HttpRequestMessage clone = new(original.Method, original.RequestUri);
        foreach (KeyValuePair<string, IEnumerable<string>> header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (original.Content is not null)
        {
            byte[] buffer = await original.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            ByteArrayContent content = new(buffer);
            foreach (KeyValuePair<string, IEnumerable<string>> header in original.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }
        return clone;
    }

    public sealed record RateLimitState(
        int Remaining,
        int Limit,
        DateTimeOffset? ResetAt,
        DateTimeOffset LastUpdated
    );

    public sealed record RateLimitSnapshot(
        int Remaining,
        int Limit,
        DateTimeOffset? ResetAt,
        DateTimeOffset LastUpdated
    );
}
