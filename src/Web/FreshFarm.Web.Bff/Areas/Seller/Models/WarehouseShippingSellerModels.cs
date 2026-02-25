using Microsoft.AspNetCore.Mvc.Rendering;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class WarehouseStatsViewModel
{
    public int TotalProducts { get; set; }

    public int LowStockCount { get; set; }

    public int OutOfStockCount { get; set; }

    public decimal TotalWarehouseValue { get; set; }

    public List<WarehouseViewModel> Products { get; set; } = new();
}

public sealed class WarehouseImportViewModel
{
    public string ImportDate { get; set; } = string.Empty;

    public IEnumerable<SelectListItem> ProductOptions { get; set; } = Enumerable.Empty<SelectListItem>();
}

public sealed class WarehouseViewModel
{
    public int WarehouseID { get; set; }

    public int ProductID { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string SKU { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public string ImageFileName { get; set; } = "no-image.png";

    public int StockQuantity { get; set; }

    public int ShelfQuantity { get; set; }

    public int WarehouseQuantity { get; set; }

    public int MaxStock { get; set; }

    public bool IsManuallyDisabled { get; set; }

    public decimal ImportPrice { get; set; }

    public decimal SellPrice { get; set; }

    public string? SupplierName { get; set; }

    public DateTime? ImportDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    public string? Notes { get; set; }

    public int NearExpiryDays { get; set; } = 7;

    public int EffectiveNearExpiryDays => NearExpiryDays <= 0 ? 7 : NearExpiryDays;

    public int? DaysUntilExpiry
    {
        get
        {
            if (!ExpiryDate.HasValue)
            {
                return null;
            }

            return (ExpiryDate.Value.Date - DateTime.Today).Days;
        }
    }

    public bool IsExpired => DaysUntilExpiry.HasValue && DaysUntilExpiry.Value < 0;

    public bool IsNearExpiry => DaysUntilExpiry.HasValue && DaysUntilExpiry.Value >= 0 && DaysUntilExpiry.Value <= EffectiveNearExpiryDays;

    public string ExpiryStatusText
    {
        get
        {
            if (!ExpiryDate.HasValue)
            {
                return "Khong xac dinh";
            }

            if (IsExpired)
            {
                return "Het han";
            }

            if (IsNearExpiry)
            {
                return "Sap het han";
            }

            return "Con han";
        }
    }

    public string ExpiryStatusClass
    {
        get
        {
            if (!ExpiryDate.HasValue)
            {
                return "bg-secondary text-white";
            }

            if (IsExpired)
            {
                return "bg-danger text-white";
            }

            if (IsNearExpiry)
            {
                return "bg-warning text-dark";
            }

            return "bg-success text-white";
        }
    }

    public string StockStatus
    {
        get
        {
            if (StockQuantity == 0)
            {
                return "Het hang";
            }

            if (StockQuantity <= 10)
            {
                return "Sap het";
            }

            return "Con hang";
        }
    }

    public string StockStatusClass
    {
        get
        {
            if (StockQuantity == 0)
            {
                return "badge bg-danger";
            }

            if (StockQuantity <= 10)
            {
                return "badge bg-warning text-dark";
            }

            return "badge bg-success";
        }
    }
}

public sealed class WarehouseTransactionViewModel
{
    public int TransactionID { get; set; }

    public string TransactionCode { get; set; } = string.Empty;

    public string TransactionType { get; set; } = string.Empty;

    public DateTime TransactionDate { get; set; }

    public int TotalQuantity { get; set; }

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<WarehouseTransactionDetailViewModel> Details { get; set; } = new();
}

public sealed class WarehouseTransactionDetailViewModel
{
    public int DetailID { get; set; }

    public int ProductID { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string SKU { get; set; } = string.Empty;

    public string ImageFileName { get; set; } = "no-image.png";

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public string? BatchCode { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public string? Notes { get; set; }
}

public sealed class WarehouseHistory
{
    public int WarehouseID { get; set; }

    public string TransactionType { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public string? Reason { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedDate { get; set; }

    public string? Notes { get; set; }
}

public sealed class Warehouse
{
    public int WarehouseID { get; set; }

    public string? BatchCode { get; set; }
}

public sealed class Shipping
{
    public int ShippingID { get; set; }

    public int OrderID { get; set; }

    public string ShippingType { get; set; } = "Giao noi bo";

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? AddressDetail { get; set; }

    public int? ProvinceId { get; set; }

    public int? CommuneId { get; set; }

    public bool IsStorePickup { get; set; }

    public string? StoreAddress { get; set; }

    public Province? Province { get; set; }

    public Commune? Commune { get; set; }

    public Order? Order { get; set; }
}

public sealed class Order
{
    public int OrderID { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<Payment> Payments { get; set; } = new();

    public List<DeliveryAssignment> DeliveryAssignments { get; set; } = new();
}

public sealed class Payment
{
    public int PaymentID { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;

    public string? PaymentStatus { get; set; }

    public DateTime? PaymentDate { get; set; }
}

public sealed class DeliveryAssignment
{
    public bool IsActive { get; set; }

    public int? AssignedToAdminID { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public UserAdmin? UserAdmin { get; set; }
}

public sealed class UserAdmin
{
    public int AdminID { get; set; }

    public string? FullName { get; set; }

    public string? UserName { get; set; }
}

public sealed class Province
{
    public int ProvinceId { get; set; }

    public string ProvinceName { get; set; } = string.Empty;
}

public sealed class Commune
{
    public int CommuneId { get; set; }

    public string CommuneName { get; set; } = string.Empty;
}
