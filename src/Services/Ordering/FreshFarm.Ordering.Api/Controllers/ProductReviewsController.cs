using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FreshFarm.Ordering.Api.Dtos;
using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/product-reviews")]
public sealed class ProductReviewsController : ControllerBase
{
    private static readonly string[] IneligibleOrderStatuses = ["awaitingpayment", "expired", "canceled", "cancelled"];
    private static readonly string[] IneligiblePaymentStatuses = ["failed", "expired"];

    private readonly FreshFarmOrderingDBContext _db;

    public ProductReviewsController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet("products/{productId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByProduct([FromRoute] int productId, CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return BadRequest(new { message = "Sản phẩm không hợp lệ." });
        }

        var userId = TryGetUserIdFromToken();
        var approvedReviews = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId)
            .Where(r => !r.IsDeleted && r.IsApproved)
            .Where(r => !r.ReplyTo.HasValue)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var rootReviewIds = approvedReviews
            .Select(r => r.ReviewId)
            .ToList();

        var repliesByParent = rootReviewIds.Count == 0
            ? new Dictionary<int, List<Review>>()
            : await _db.Reviews
                .AsNoTracking()
                .Where(r => !r.IsDeleted && r.IsApproved)
                .Where(r => r.ReplyTo.HasValue && rootReviewIds.Contains(r.ReplyTo.Value))
                .OrderBy(r => r.CreatedAt)
                .GroupBy(r => r.ReplyTo!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.ToList(), cancellationToken);

        Review? existingOwnReview = null;
        if (userId.HasValue)
        {
            existingOwnReview = await _db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId == productId && r.UserId == userId.Value)
                .Where(r => !r.IsDeleted && !r.ReplyTo.HasValue)
                .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var displayUserIds = approvedReviews
            .Select(r => r.UserId)
            .Distinct()
            .ToList();

        if (userId.HasValue && !displayUserIds.Contains(userId.Value))
        {
            displayUserIds.Add(userId.Value);
        }

        var latestOrdersByUser = displayUserIds.Count == 0
            ? new Dictionary<int, Order>()
            : await _db.Orders
                .AsNoTracking()
                .Where(o => displayUserIds.Contains(o.UserId))
                .OrderByDescending(o => o.OrderDate)
                .GroupBy(o => o.UserId)
                .Select(g => g.First())
                .ToDictionaryAsync(o => o.UserId, cancellationToken);

        var hasPurchased = userId.HasValue && await HasEligiblePurchaseAsync(userId.Value, productId, cancellationToken);
        var viewer = BuildViewer(userId.HasValue, hasPurchased, existingOwnReview);
        var response = new ProductReviewPageResponse
        {
            Summary = new ProductReviewSummaryDto
            {
                ProductId = productId,
                TotalReviews = approvedReviews.Count,
                AverageRating = approvedReviews.Count == 0
                    ? 0m
                    : decimal.Round((decimal)approvedReviews.Average(r => r.Rating), 1),
                Stars = Enumerable.Range(1, 5)
                    .OrderByDescending(x => x)
                    .Select(star => new ProductReviewStarCountDto
                    {
                        Rating = star,
                        Count = approvedReviews.Count(r => r.Rating == star)
                    })
                    .ToList()
            },
            Viewer = viewer,
            Reviews = approvedReviews
                .Select(review => MapReview(review, latestOrdersByUser, repliesByParent, userId))
                .ToList()
        };

        return Ok(response);
    }

    [Authorize]
    [HttpPost("products/{productId:int}")]
    public async Task<IActionResult> Upsert(
        [FromRoute] int productId,
        [FromBody] UpsertProductReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0)
        {
            return BadRequest(new { message = "Sản phẩm không hợp lệ." });
        }

        var userId = TryGetUserIdFromToken();
        if (!userId.HasValue)
        {
            return Unauthorized(new { message = "Không xác định được người dùng." });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var canReview = await HasEligiblePurchaseAsync(userId.Value, productId, cancellationToken);
        if (!canReview)
        {
            return BadRequest(new { message = "Bạn cần mua sản phẩm này trước khi gửi đánh giá." });
        }

        var normalizedComment = request.Comment.Trim();
        var now = DateTime.UtcNow;
        var review = await _db.Reviews
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId.Value && !r.IsDeleted && !r.ReplyTo.HasValue, cancellationToken);

        var isUpdate = review is not null;
        if (review is null)
        {
            review = new Review
            {
                ProductId = productId,
                UserId = userId.Value,
                Rating = request.Rating,
                Comment = normalizedComment,
                CreatedAt = now,
                IsApproved = true,
                IsEdited = false,
                IsDeleted = false
            };

            _db.Reviews.Add(review);
        }
        else
        {
            review.Rating = request.Rating;
            review.Comment = normalizedComment;
            review.IsApproved = true;
            review.IsEdited = true;
            review.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            message = isUpdate ? "Đánh giá của bạn đã được cập nhật." : "Cảm ơn bạn đã gửi đánh giá.",
            reviewId = review.ReviewId
        });
    }

    private async Task<bool> HasEligiblePurchaseAsync(int userId, int productId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .AsNoTracking()
            .Where(o => o.UserId == userId)
            .Where(o => !IneligibleOrderStatuses.Contains((o.Status ?? string.Empty).Trim().ToLower()))
            .Where(o => !IneligiblePaymentStatuses.Contains((o.PaymentStatus ?? string.Empty).Trim().ToLower()))
            .AnyAsync(o => o.OrderDetails.Any(od => od.ProductId == productId), cancellationToken);
    }

    private static ProductReviewViewerDto BuildViewer(bool isAuthenticated, bool hasPurchased, Review? existingOwnReview)
    {
        if (!isAuthenticated)
        {
            return new ProductReviewViewerDto
            {
                IsAuthenticated = false,
                HasPurchased = false,
                CanSubmitReview = false,
                Message = "Đăng nhập và mua sản phẩm để gửi đánh giá của bạn."
            };
        }

        if (!hasPurchased)
        {
            return new ProductReviewViewerDto
            {
                IsAuthenticated = true,
                HasPurchased = false,
                CanSubmitReview = false,
                Message = "Bạn chưa mua sản phẩm này nên hiện chỉ có thể xem đánh giá của khách hàng khác."
            };
        }

        return new ProductReviewViewerDto
        {
            IsAuthenticated = true,
            HasPurchased = true,
            CanSubmitReview = true,
            Message = existingOwnReview is null
                ? "Bạn đã mua sản phẩm này. Hãy chia sẻ trải nghiệm để giúp người mua khác dễ chọn hơn."
                : "Bạn đã từng đánh giá sản phẩm này. Bạn có thể cập nhật lại nội dung nếu muốn.",
            ExistingReview = existingOwnReview is null
                ? null
                : new ProductReviewDraftDto
                {
                    ReviewId = existingOwnReview.ReviewId,
                    Rating = existingOwnReview.Rating,
                    Comment = existingOwnReview.Comment ?? string.Empty,
                    CreatedAt = existingOwnReview.CreatedAt,
                    UpdatedAt = existingOwnReview.UpdatedAt
                }
        };
    }

    private static ProductReviewItemDto MapReview(
        Review review,
        IReadOnlyDictionary<int, Order> latestOrdersByUser,
        IReadOnlyDictionary<int, List<Review>> repliesByParent,
        int? currentUserId)
    {
        latestOrdersByUser.TryGetValue(review.UserId, out var latestOrder);
        var displayName = string.IsNullOrWhiteSpace(latestOrder?.BuyerFullName)
            ? $"Khách {review.UserId}"
            : latestOrder!.BuyerFullName;

        return new ProductReviewItemDto
        {
            ReviewId = review.ReviewId,
            Rating = review.Rating,
            Comment = review.Comment ?? string.Empty,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt,
            IsEdited = review.IsEdited,
            IsOwner = currentUserId.HasValue && review.UserId == currentUserId.Value,
            Author = new ProductReviewAuthorDto
            {
                UserId = review.UserId,
                DisplayName = displayName
            },
            Replies = repliesByParent.TryGetValue(review.ReviewId, out var replies)
                ? replies.Select(MapReply).ToList()
                : new List<ProductReviewReplyDto>()
        };
    }

    private static ProductReviewReplyDto MapReply(Review reply)
    {
        return new ProductReviewReplyDto
        {
            ReviewId = reply.ReviewId,
            Comment = reply.Comment ?? string.Empty,
            CreatedAt = reply.CreatedAt,
            UpdatedAt = reply.UpdatedAt,
            IsEdited = reply.IsEdited,
            Author = new ProductReviewAuthorDto
            {
                UserId = reply.UserId,
                DisplayName = "Shop phản hồi"
            }
        };
    }

    private int? TryGetUserIdFromToken()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");

        return int.TryParse(sub, out var userId) ? userId : null;
    }
}
