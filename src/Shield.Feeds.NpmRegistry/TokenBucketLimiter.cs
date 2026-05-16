namespace Shield.Feeds.NpmRegistry;

public sealed class TokenBucketLimiter : IDisposable
{
    private readonly int _capacity;
    private readonly TimeSpan _refillPeriod;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _tokenGate;
    private readonly ITimer _refillTimer;
    private int _availableTokens;

    public TokenBucketLimiter(int tokensPerSecond, TimeProvider? time = null)
    {
        _capacity = Math.Max(1, tokensPerSecond);
        _refillPeriod = TimeSpan.FromSeconds(1);
        _time = time ?? TimeProvider.System;
        _availableTokens = _capacity;
        _tokenGate = new SemaphoreSlim(_capacity, _capacity);
        _refillTimer = _time.CreateTimer(Refill, null, _refillPeriod, _refillPeriod);
    }

    public async ValueTask AcquireAsync(CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        Interlocked.Decrement(ref _availableTokens);
    }

    private void Refill(object? _)
    {
        int missing = _capacity - Volatile.Read(ref _availableTokens);
        for (int i = 0; i < missing; i++)
        {
            Interlocked.Increment(ref _availableTokens);
            _tokenGate.Release();
        }
    }

    public void Dispose()
    {
        _refillTimer.Dispose();
        _tokenGate.Dispose();
    }
}
