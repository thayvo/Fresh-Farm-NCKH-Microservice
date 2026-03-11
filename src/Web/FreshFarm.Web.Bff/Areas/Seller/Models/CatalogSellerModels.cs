using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class Product
{
    public int ProductID { get; set; }

    [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
    [StringLength(255, ErrorMessage = "Tên sản phẩm tối đa 255 ký tự")]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "SKU tối đa 50 ký tự")]
    public string? Sku { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Giá phải lớn hơn hoặc bằng 0")]
    public decimal Price { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đơn vị")]
    public int UnitID { get; set; }

    public bool Status { get; set; } = true;

    public int StockQuantity { get; set; }

    public string? ImageFileName { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    [StringLength(300, ErrorMessage = "Mô tả ngắn tối đa 300 ký tự")]
    public string? ShortDescription { get; set; }

    [StringLength(4000, ErrorMessage = "Mô tả chi tiết quá dài")]
    public string? LongDescription { get; set; }

    public bool IsManuallyDisabled { get; set; }

    [ValidateNever]
    public Category Category { get; set; } = new();

    [ValidateNever]
    public Unit Unit { get; set; } = new();

    [ValidateNever]
    public List<ProductInfo> ProductInfoes { get; set; } = new();
}

public sealed class ProductInfo
{
    public string? Weight { get; set; }

    public string? Origin { get; set; }

    public string? Standard { get; set; }

    public string? Preservation { get; set; }
}

public sealed class Category
{
    public int CategoryID { get; set; }

    [Required(ErrorMessage = "Tên danh mục là bắt buộc")]
    [StringLength(100, ErrorMessage = "Tên danh mục tối đa 100 ký tự")]
    public string CategoryName { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự")]
    public string? Description { get; set; }

    public string? ImageCategoriesName { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Slug { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;

    public DateTime? UpdatedDate { get; set; }
}

public sealed class Unit
{
    public int UnitID { get; set; }

    public string UnitName { get; set; } = string.Empty;

    public string Symbol { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedDate { get; set; }
}

public sealed class UnitViewModel
{
    public int UnitID { get; set; }

    [Required(ErrorMessage = "Tên đơn vị là bắt buộc")]
    [StringLength(100, ErrorMessage = "Tên đơn vị tối đa 100 ký tự")]
    public string UnitName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ký hiệu là bắt buộc")]
    [StringLength(20, ErrorMessage = "Ký hiệu tối đa 20 ký tự")]
    public string Symbol { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả tối đa 500 ký tự")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; }

    public string UnitType { get; set; } = "Khác";
}

public sealed class UnitStatsViewModel
{
    public int TotalUnits { get; set; }

    public int ActiveUnits { get; set; }

    public int InactiveUnits { get; set; }

    public int NewUnitsThisMonth { get; set; }
}
