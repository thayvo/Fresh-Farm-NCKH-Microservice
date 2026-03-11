namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class ProductModerationPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string State { get; set; } = "all";

    public string Risk { get; set; } = "all";

    public string Keyword { get; set; } = string.Empty;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;

    public int Total { get; set; }

    public int TotalPages { get; set; }

    public int TotalProducts { get; set; }

    public int FlaggedProducts { get; set; }

    public int HighRiskProducts { get; set; }

    public int HiddenProducts { get; set; }

    public int CleanProducts { get; set; }

    public string[] KeywordSuggestions { get; set; } = Array.Empty<string>();

    public List<ProductModerationRowViewModel> Rows { get; set; } = new();
}

public sealed class ProductModerationRowViewModel
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }

    public bool Status { get; set; }

    public bool IsManuallyDisabled { get; set; }

    public string? ImageFileName { get; set; }

    public string ShortDescription { get; set; } = string.Empty;

    public string LongDescription { get; set; } = string.Empty;

    public string ModerationState { get; set; } = string.Empty;

    public string RiskLevel { get; set; } = string.Empty;

    public List<string> MatchedKeywords { get; set; } = new();

    public List<string> Issues { get; set; } = new();
}
