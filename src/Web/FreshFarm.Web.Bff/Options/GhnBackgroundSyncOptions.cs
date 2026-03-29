namespace FreshFarm.Web.Bff.Options;

public sealed class GhnBackgroundSyncOptions
{
    public const string SectionName = "BackgroundJobs:GhnSync";

    public bool Enabled { get; set; } = true;

    public int StartupDelaySeconds { get; set; } = 90;

    public int IntervalMinutes { get; set; } = 5;

    public int StaleMinutes { get; set; } = 20;

    public int BatchSize { get; set; } = 10;
}
