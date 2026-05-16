using System.Collections.Concurrent;
using Shield.Core.Domain;

namespace Shield.Api.Services;

// Short-lived PKCE state cache. Entries expire after StateTtl; a single timer sweeps
// the dictionary every minute. Singleton — the OAuth start/callback round-trip is
// usually under a minute so memory pressure is trivial.
public interface IOAuthStateStore
{
    void Save(string state, OAuthStateEntry entry);
    OAuthStateEntry? Consume(string state);
}

public sealed record OAuthStateEntry(
    OAuthProvider Provider,
    string CodeVerifier,
    string ReturnUrl,
    DateTime ExpiresAt,
    OAuthIntent Intent = OAuthIntent.Connect
);

public enum OAuthIntent
{
    // Operator linking an integration to an already-signed-in admin user (legacy default).
    Connect = 0,

    // Anonymous browser landing on /login choosing GitHub/Slack/Google to sign in.
    Signin = 1,
}

public sealed class OAuthStateStore : IOAuthStateStore, IDisposable
{
    public static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, OAuthStateEntry> _entries = new();
    private readonly Timer _sweeper;

    public OAuthStateStore()
    {
        _sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void Save(string state, OAuthStateEntry entry) => _entries[state] = entry;

    public OAuthStateEntry? Consume(string state)
    {
        if (!_entries.TryRemove(state, out OAuthStateEntry? entry))
            return null;
        return entry.ExpiresAt < DateTime.UtcNow ? null : entry;
    }

    private void Sweep()
    {
        DateTime now = DateTime.UtcNow;
        foreach (KeyValuePair<string, OAuthStateEntry> kvp in _entries)
        {
            if (kvp.Value.ExpiresAt < now)
                _entries.TryRemove(kvp.Key, out _);
        }
    }

    public void Dispose() => _sweeper.Dispose();
}
