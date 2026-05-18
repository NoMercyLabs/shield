using System.Text.Json;

namespace Shield.Matcher.Versioning;

public sealed record VersionRange(
    string? GtOrEq = null,
    string? Lt = null,
    string? GtOrEqExclusive = null,
    string? LtOrEq = null,
    IReadOnlyList<string>? Exact = null)
{
    public static IReadOnlyList<VersionRange> ParseOsvEvents(string eventsJson)
    {
        ArgumentNullException.ThrowIfNull(eventsJson);
        if (string.IsNullOrWhiteSpace(eventsJson))
            return [];

        using JsonDocument document = JsonDocument.Parse(eventsJson);
        return ParseOsvEvents(document.RootElement);
    }

    public static IReadOnlyList<VersionRange> ParseOsvEvents(JsonElement events)
    {
        if (events.ValueKind != JsonValueKind.Array)
            return [];

        List<VersionRange> ranges = [];
        string? introduced = null;
        List<string>? exact = null;

        foreach (JsonElement evt in events.EnumerateArray())
        {
            if (evt.ValueKind != JsonValueKind.Object)
                continue;

            if (evt.TryGetProperty("introduced", out JsonElement introducedEl))
            {
                string? value = introducedEl.GetString();
                if (value == "0")
                    introduced = null;
                else
                    introduced = value;
            }
            else if (evt.TryGetProperty("fixed", out JsonElement fixedEl))
            {
                string? fixedVersion = fixedEl.GetString();
                ranges.Add(new(GtOrEq: introduced, Lt: fixedVersion));
                introduced = null;
            }
            else if (evt.TryGetProperty("last_affected", out JsonElement lastEl))
            {
                string? lastAffected = lastEl.GetString();
                ranges.Add(new(GtOrEq: introduced, LtOrEq: lastAffected));
                introduced = null;
            }
            else if (evt.TryGetProperty("limit", out JsonElement limitEl))
            {
                string? limit = limitEl.GetString();
                ranges.Add(new(GtOrEq: introduced, Lt: limit));
                introduced = null;
            }
        }

        if (introduced is not null)
            ranges.Add(new(GtOrEq: introduced));

        if (exact is { Count: > 0 })
            ranges.Add(new(Exact: exact));

        return ranges;
    }

    public static VersionRange Exactly(params string[] versions)
        => new(Exact: versions);
}
