namespace FreshFarm.Ordering.Api.Options;

public sealed class RecommendationMlOptions
{
    public const string SectionName = "RecommendationMl";

    public bool Enabled { get; set; }

    public bool MaterializeUserProductScores { get; set; } = true;

    public bool PersistModelArtifact { get; set; } = true;

    public bool ExcludePreviouslyPurchasedProducts { get; set; }

    public string ModelOutputPath { get; set; } = "App_Data/recommendation-user-product-ml.zip";

    public int LookbackDays { get; set; } = 365;

    public int TrainingIntervalMinutes { get; set; } = 360;

    public int MinInteractionRows { get; set; } = 20;

    public int MinDistinctUsers { get; set; } = 2;

    public int MinDistinctProducts { get; set; } = 2;

    public int MaxTrainingPairs { get; set; } = 50_000;

    public int MaxUsersPerRefresh { get; set; } = 2_000;

    public int MaxCandidateProducts { get; set; } = 1_000;

    public int TopNPerUser { get; set; } = 48;

    public int NumberOfIterations { get; set; } = 20;

    public int ApproximationRank { get; set; } = 64;

    public double Alpha { get; set; } = 0.01d;

    public double Lambda { get; set; } = 0.025d;

    public double C { get; set; } = 0.00001d;

    public int RandomSeed { get; set; } = 42;

    public float ViewWeight { get; set; } = 1f;

    public float SearchClickWeight { get; set; } = 3f;

    public float RecommendationClickWeight { get; set; } = 4f;

    public float PurchaseWeight { get; set; } = 10f;

    public int NormalizedScoreFloor { get; set; } = 20;

    public int NormalizedScoreCeiling { get; set; } = 100;
}
