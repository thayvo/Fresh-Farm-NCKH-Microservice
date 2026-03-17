using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Ordering.Api.Dtos;

public sealed class UpsertProductReviewRequest
{
    [Range(1, 5, ErrorMessage = "Số sao phải trong khoảng từ 1 đến 5.")]
    public int Rating { get; init; }

    [Required(ErrorMessage = "Nội dung đánh giá không được để trống.")]
    [StringLength(1000, MinimumLength = 5, ErrorMessage = "Nội dung đánh giá phải từ 5 đến 1000 ký tự.")]
    public string Comment { get; init; } = string.Empty;
}

public sealed class ProductReviewPageResponse
{
    public ProductReviewSummaryDto Summary { get; init; } = new();
    public ProductReviewViewerDto Viewer { get; init; } = new();
    public List<ProductReviewItemDto> Reviews { get; init; } = new();
}

public sealed class ProductReviewSummaryDto
{
    public int ProductId { get; init; }
    public int TotalReviews { get; init; }
    public decimal AverageRating { get; init; }
    public List<ProductReviewStarCountDto> Stars { get; init; } = new();
}

public sealed class ProductReviewStarCountDto
{
    public int Rating { get; init; }
    public int Count { get; init; }
}

public sealed class ProductReviewViewerDto
{
    public bool IsAuthenticated { get; init; }
    public bool HasPurchased { get; init; }
    public bool CanSubmitReview { get; init; }
    public string Message { get; init; } = string.Empty;
    public ProductReviewDraftDto? ExistingReview { get; init; }
}

public sealed class ProductReviewDraftDto
{
    public int ReviewId { get; init; }
    public int Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed class ProductReviewItemDto
{
    public int ReviewId { get; init; }
    public int Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public bool IsEdited { get; init; }
    public bool IsOwner { get; init; }
    public ProductReviewAuthorDto Author { get; init; } = new();
    public List<ProductReviewReplyDto> Replies { get; init; } = new();
}

public sealed class ProductReviewReplyDto
{
    public int ReviewId { get; init; }
    public string Comment { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public bool IsEdited { get; init; }
    public ProductReviewAuthorDto Author { get; init; } = new();
}

public sealed class ProductReviewAuthorDto
{
    public int UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}
