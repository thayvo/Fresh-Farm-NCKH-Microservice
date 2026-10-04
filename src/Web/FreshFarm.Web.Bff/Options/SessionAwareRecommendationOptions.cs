namespace FreshFarm.Web.Bff.Options;

public sealed class SessionAwareRecommendationOptions
{
    public const string SectionName = "SessionAwareRecommendation";

    public string RedisKeyPrefix { get; set; } = "recommendation:session:";

    public int MaxCandidates { get; set; } = 50;

    public double CandidateSellerDiversityAlpha { get; set; } = 0.4;

    public double CandidateCategoryDiversityBeta { get; set; } = 0.25;

    public double CandidateDiversityMinMultiplier { get; set; } = 0.5;

    public int MaxRecentSearches { get; set; } = 10;

    public int MaxRecentClicks { get; set; } = 20;

    public int SessionSignalTtlMinutes { get; set; } = 30;

    public int MaxSearchTokens { get; set; } = 24;

    public int SessionCacheReadTimeoutMilliseconds { get; set; } = 30;

    public double SearchKeywordBoost { get; set; } = 13;

    public double RecentClickedProductBoost { get; set; } = 24;

    public double RecentClickedSellerBoost { get; set; } = 7;

    public double RecentClickedCategoryBoost { get; set; } = 5;

    public double TopPositionBoostFactor { get; set; } = 0.65;

    public double MidPositionBoostFactor { get; set; } = 1.0;

    public double CtrWeight { get; set; } = 0.12;

    public double AddToCartWeight { get; set; } = 0.08;

    public double PurchaseWeight { get; set; } = 0.35;

    public double RevenueWeight { get; set; } = 0.45;

    public double ObjectiveScoreScale { get; set; } = 30;

    public int ObjectiveMetricsLookbackDays { get; set; } = 7;

    public int NegativeFeedbackLookbackDays { get; set; } = 14;

    public double NegativeFeedbackPenaltyScale { get; set; } = 12;

    public double InStockBoost { get; set; } = 4;

    public double OutOfStockPenalty { get; set; } = 35;

    public double NearbyShopBoost { get; set; } = 2;

    public double NearbyShopRadiusKm { get; set; } = 12;

    public int WarnIfSlowerThanMilliseconds { get; set; } = 40;
}
