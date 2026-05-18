namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class SellerReviewViewModel
{
    public int ReviewID { get; set; }

    public int? ReplyTo { get; set; }

    public int UserID { get; set; }

    public int ProductID { get; set; }

    public int Rating { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public bool IsApproved { get; set; }

    public bool IsEdited { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public SellerReviewUserViewModel? User { get; set; }

    public SellerReviewProductViewModel? Product { get; set; }

    public List<SellerReviewReportViewModel> ReviewReports { get; set; } = new();
}

public sealed class SellerReviewUserViewModel
{
    public int UserID { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string? FullName { get; set; }

    public string? AvatarUrl { get; set; }
}

public sealed class SellerReviewProductViewModel
{
    public int ProductID { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? ImageFileName { get; set; }
}

public sealed class SellerReviewReportViewModel
{
    public int ReviewReportID { get; set; }

    public int ReviewID { get; set; }

    public int ReporterUserID { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class SellerReportedReviewRowViewModel
{
    public int ReviewID { get; set; }

    public int OpenCount { get; set; }

    public DateTime FirstReportAt { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string? ProductImageFileName { get; set; }

    public int Rating { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
