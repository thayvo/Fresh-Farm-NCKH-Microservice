using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Catalog.Api.Dtos;

public sealed class CategoryUpsertRequest
{
    [Required(ErrorMessage = "Tên danh mục là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Tên danh mục không được vượt quá 100 ký tự.")]
    public string CategoryName { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    [StringLength(255, ErrorMessage = "Tên ảnh danh mục không được vượt quá 255 ký tự.")]
    public string? ImageCategoriesName { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(255, ErrorMessage = "Slug không được vượt quá 255 ký tự.")]
    public string? Slug { get; set; }
}
