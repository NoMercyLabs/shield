namespace Shield.Core.Options;

public sealed class ShieldOptions
{
    public const string SectionName = "Shield";

    public DataSourceOptions DataSource { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();
}

public sealed class DataSourceOptions
{
    public string Shield { get; set; } = "Data Source=shield.db";
    public string Feeds { get; set; } = "Data Source=feeds.db";
}

public sealed class RetentionOptions
{
    public TimeSpan ResolvedFindings { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan AlertEvents { get; set; } = TimeSpan.FromDays(30);
}
