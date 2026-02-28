namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class SellerStatusViewModel
{
    public int StatusID { get; set; }

    public string StatusName { get; set; } = string.Empty;

    public int StatusTypeID { get; set; }

    public string ColorCode { get; set; } = "#28a745";

    public string Note { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; }

    public SellerStatusTypeViewModel StatusType { get; set; } = new();
}

public sealed class SellerStatusTypeViewModel
{
    public int StatusTypeID { get; set; }

    public string StatusTypeName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; }

    public int ActiveStatusCount { get; set; }
}
