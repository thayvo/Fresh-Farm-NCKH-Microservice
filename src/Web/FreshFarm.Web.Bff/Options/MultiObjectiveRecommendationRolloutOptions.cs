namespace FreshFarm.Web.Bff.Options;

public sealed class MultiObjectiveRecommendationRolloutOptions
{
    public const string SectionName = "BackgroundJobs:MultiObjectiveRecommendationRollout";

    public bool Enabled { get; set; } = true;

    public int StartupDelaySeconds { get; set; } = 300;

    public int IntervalMinutes { get; set; } = 60;

    public int LookbackHours { get; set; } = 6;

    public int MinimumImpressions { get; set; } = 200;

    public int MinimumGuardrailAgeHours { get; set; } = 3;

    public int SmoothingWindowHours { get; set; } = 3;

    public int InitialTrafficPercent { get; set; } = 20;

    public int[] RampSteps { get; set; } = new[] { 20, 50, 100 };

    public int StableWindowCountForRamp { get; set; } = 3;

    public int StableHoursForRamp { get; set; } = 24;

    public int StableHoursForLock { get; set; } = 48;

    public int RetainedConfigVersionCount { get; set; } = 3;

    public double CtrDropRevenueAdjustRatio { get; set; } = 0.10;

    public double PurchaseRateDropRollbackRatio { get; set; } = 0.05;

    public double RevenueWeightAdjustmentRatio { get; set; } = 0.10;

    public int RequestTimeoutSeconds { get; set; } = 5;
}
