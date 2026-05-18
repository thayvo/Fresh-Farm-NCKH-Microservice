namespace FreshFarm.Web.Bff.Options;

public sealed class RecommendationExperimentOptions
{
    public const string SectionName = "RecommendationExperiment";

    public int SessionRerankTrafficPercent { get; set; } = 20;
}
