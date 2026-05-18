using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Shield.Api.Http;
using Xunit;

namespace Shield.Api.Tests;

// Validates the three layered policies:
// - Secondary rate-limit (429 + Retry-After OR 403 + Retry-After): one retry after the
//   header-specified delay.
// - Primary rate-limit (403 + X-RateLimit-Remaining: 0): sleep until reset.
// - Proactive primary throttle (remaining < threshold) on the next call to the same principal.
public sealed class GitHubRateLimitHandlerTests
{
    [Fact]
    public async Task Retries_once_after_429_with_retry_after()
    {
        StubHandler stub = new();
        // First response: 429 + Retry-After: 1. Second: 200.
        stub.Enqueue(BuildResponse(HttpStatusCode.TooManyRequests, retryAfterSeconds: 1));
        stub.Enqueue(BuildResponse(HttpStatusCode.OK, remaining: 4000, limit: 5000));

        TestClock clock = new();
        List<TimeSpan> sleeps = [];
        GitHubRateLimitHandler handler = NewHandler(stub, clock, sleeps);

        HttpResponseMessage response = await SendAsync(handler, "https://api.github.com/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.SendCount.Should().Be(2);
        sleeps.Should().ContainSingle();
        sleeps[0].Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Retries_once_on_403_with_retry_after()
    {
        StubHandler stub = new();
        stub.Enqueue(BuildResponse(HttpStatusCode.Forbidden, retryAfterSeconds: 2));
        stub.Enqueue(BuildResponse(HttpStatusCode.OK, remaining: 4000, limit: 5000));

        List<TimeSpan> sleeps = [];
        GitHubRateLimitHandler handler = NewHandler(stub, new(), sleeps);

        HttpResponseMessage response = await SendAsync(handler, "https://api.github.com/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.SendCount.Should().Be(2);
        sleeps.Should().ContainSingle().Which.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Sleeps_until_reset_on_primary_exhaustion()
    {
        TestClock clock = new();
        long unixReset = clock.GetUtcNow().AddSeconds(5).ToUnixTimeSeconds();

        StubHandler stub = new();
        stub.Enqueue(
            BuildResponse(HttpStatusCode.Forbidden, remaining: 0, limit: 5000, resetUnix: unixReset)
        );
        stub.Enqueue(BuildResponse(HttpStatusCode.OK, remaining: 4999, limit: 5000));

        List<TimeSpan> sleeps = [];
        GitHubRateLimitHandler handler = NewHandler(stub, clock, sleeps);

        HttpResponseMessage response = await SendAsync(handler, "https://api.github.com/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sleeps.Should().HaveCount(1);
        sleeps[0].Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Proactively_sleeps_when_remaining_below_threshold()
    {
        // First call returns 200 with remaining=10 — that primes the per-principal state.
        // Second call to the SAME principal must sleep before sending.
        TestClock clock = new();
        long unixReset = clock.GetUtcNow().AddSeconds(3).ToUnixTimeSeconds();

        StubHandler stub = new();
        stub.Enqueue(
            BuildResponse(HttpStatusCode.OK, remaining: 10, limit: 5000, resetUnix: unixReset)
        );
        stub.Enqueue(BuildResponse(HttpStatusCode.OK, remaining: 5000, limit: 5000));

        List<TimeSpan> sleeps = [];
        GitHubRateLimitHandler handler = NewHandler(stub, clock, sleeps);

        HttpResponseMessage first = await SendAsync(
            handler,
            "https://api.github.com/user",
            token: "bearer-abc"
        );
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        sleeps.Should().BeEmpty();

        HttpResponseMessage second = await SendAsync(
            handler,
            "https://api.github.com/user",
            token: "bearer-abc"
        );
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        sleeps.Should().ContainSingle();
        sleeps[0].Should().BeGreaterThan(TimeSpan.Zero);
        sleeps[0].Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Distinct_tokens_get_distinct_buckets()
    {
        // Token A's bucket near-empty must not slow down Token B's call.
        TestClock clock = new();
        long unixReset = clock.GetUtcNow().AddSeconds(60).ToUnixTimeSeconds();

        StubHandler stub = new();
        // Token A primes its bucket low.
        stub.Enqueue(
            BuildResponse(HttpStatusCode.OK, remaining: 5, limit: 5000, resetUnix: unixReset)
        );
        // Token B's call should not sleep.
        stub.Enqueue(BuildResponse(HttpStatusCode.OK, remaining: 5000, limit: 5000));

        List<TimeSpan> sleeps = [];
        GitHubRateLimitHandler handler = NewHandler(stub, clock, sleeps);

        await SendAsync(handler, "https://api.github.com/user", token: "token-a");
        await SendAsync(handler, "https://api.github.com/user", token: "token-b");

        sleeps.Should().BeEmpty();
    }

    private static HttpResponseMessage BuildResponse(
        HttpStatusCode status,
        int? remaining = null,
        int? limit = null,
        long? resetUnix = null,
        int? retryAfterSeconds = null
    )
    {
        HttpResponseMessage response = new(status);
        if (remaining is not null)
            response.Headers.TryAddWithoutValidation(
                "X-RateLimit-Remaining",
                remaining.Value.ToString()
            );
        if (limit is not null)
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", limit.Value.ToString());
        if (resetUnix is not null)
            response.Headers.TryAddWithoutValidation(
                "X-RateLimit-Reset",
                resetUnix.Value.ToString()
            );
        if (retryAfterSeconds is not null)
            response.Headers.RetryAfter = new(
                TimeSpan.FromSeconds(retryAfterSeconds.Value)
            );
        return response;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpMessageHandler handler,
        string url,
        string? token = null
    )
    {
        HttpClient http = new(handler);
        HttpRequestMessage request = new(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new("Bearer", token);
        return http.SendAsync(request);
    }

    private static GitHubRateLimitHandler NewHandler(
        StubHandler inner,
        TestClock clock,
        List<TimeSpan> sleeps
    )
    {
        GitHubRateLimitStore store = new();
        GitHubRateLimitHandler handler = new(
            store,
            NullLogger<GitHubRateLimitHandler>.Instance,
            clock,
            (wait, _) =>
            {
                sleeps.Add(wait);
                // Advance the clock so primary-rate-limit sleeps don't loop forever.
                clock.Advance(wait);
                return Task.CompletedTask;
            }
        )
        {
            InnerHandler = inner,
        };
        return handler;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public int SendCount { get; private set; }

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            SendCount++;
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more stubbed responses queued");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
