using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Dtos;

public sealed class ProductReviewUpsertRequestDto
{
    [Range(1, 5, ErrorMessage = "Vui lòng chọn số sao từ 1 đến 5.")]
    public int Rating { get; set; }

    [Required(ErrorMessage = "Nội dung đánh giá không được để trống.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Nội dung đánh giá phải từ 5 đến 1000 ký tự.")]
    public string Comment { get; set; } = string.Empty;
}
