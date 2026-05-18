namespace Shield.Core.Domain;

public class Source
{
    public int Id { get; set; }
    public SourceType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public TimeSpan ScanInterval { get; set; }
    public DateTime? LastScannedAt { get; set; }
    public string? LastError { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // JSON payload describing the remote auto-detected from `<path>/.git/config` for LocalFolder
    // sources. Null when not a working tree, parsing failed, or the host wasn't actionable.
    public string? DetectedRemote { get; set; }

    // Set when the scheduled auto-fix path (ScanQueueWorker.MaybeAutoApplyAsync) opens a PR.
    // Used by the scheduler's own 24h/7d cooldown — does NOT gate manual bulk-apply clicks.
    public DateTime? LastBulkApplyAt { get; set; }

    // Set when an operator clicks "Bulk apply" in the UI and the PR is opened. Gates the
    // manual button independently of the scheduler so a scheduled apply hours ago can never
    // surprise the user with a 429 on their first interactive click.
    public DateTime? LastManualBulkApplyAt { get; set; }

    public AutoFixMode AutoFixMode { get; set; } = AutoFixMode.Off;

    public bool IsProduction { get; set; } = false;

    // Minimum age (hours) a candidate target version must have on its registry before any
    // bulk-apply path will bump to it. Defaults to 48h to side-step the npm/typosquat window.
    // Advisory-listed fixed versions ALWAYS bypass this gate — a confirmed-malicious version
    // in the inventory must be replaced regardless of how new the fix is.
    public int MinPackageAgeHours { get; set; } = 48;
}
