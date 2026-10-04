namespace FreshFarm.Web.Bff.Options;

public sealed class SessionAwareRecommendationTuningOptions
{
    public const string SectionName = "BackgroundJobs:SessionAwareRerankTuning";

    public bool Enabled { get; set; } = true;

    public int StartupDelaySeconds { get; set; } = 180;

    public int IntervalHours { get; set; } = 24;

    public int LookbackDays { get; set; } = 7;

    public int MinimumImpressions { get; set; } = 100;

    public double MaxAdjustmentRatio { get; set; } = 0.10;

    public int SmoothingRunCount { get; set; } = 3;

    public int RetainedConfigVersionCount { get; set; } = 3;

    public double RollbackCtrDropRatio { get; set; } = 0.10;

    public double TopPositionBoostFactorMin { get; set; } = 0.5;

    public double TopPositionBoostFactorMax { get; set; } = 1.0;

    public double MidPositionBoostFactorMin { get; set; } = 1.0;

    public double MidPositionBoostFactorMax { get; set; } = 2.0;

    public int RequestTimeoutSeconds { get; set; } = 5;

    public string ConfigOverridePath { get; set; } = "App_Data/session-aware-rerank-tuning.json";
}
