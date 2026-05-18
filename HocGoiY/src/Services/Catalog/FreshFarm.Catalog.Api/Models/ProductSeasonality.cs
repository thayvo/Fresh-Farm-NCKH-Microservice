namespace FreshFarm.Catalog.Api.Models;

public class ProductSeasonality
{
    public int ProductSeasonalityId { get; set; }

    public int ProductId { get; set; }

    public string? Country { get; set; }

    public string? ProvinceRegion { get; set; }

    public string? AreaDetail { get; set; }

    public string SeasonType { get; set; } = string.Empty;

    public string SeasonLabel { get; set; } = string.Empty;

    public byte StartMonth { get; set; }

    public byte EndMonth { get; set; }

    public string? PeakMonths { get; set; }

    public bool IsYearRound { get; set; }

    public bool HasPeakSeason { get; set; }

    public bool IsControlledCultivation { get; set; }

    public bool IsImportedSeason { get; set; }

    public bool IsOffSeason { get; set; }

    public bool IsPostHarvestAvailability { get; set; }

    public int SeasonScoreWeight { get; set; }

    public string? ConfidenceLevel { get; set; }

    public string? SourceNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
}
