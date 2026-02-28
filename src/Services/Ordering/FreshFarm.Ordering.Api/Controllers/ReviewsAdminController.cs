using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/reviews")]
[Authorize(Policy = "SellerOnly")]
public sealed class ReviewsAdminController : ControllerBase
{
    private readonly FreshFarmOrderingDBContext _db;

    public ReviewsAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search = null,
        [FromQuery] int? rating = null,
        [FromQuery] int? status = null,
        [FromQuery] bool includeReplies = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var reviews = await _db.Reviews
            .AsNoTracking()
            .Where(r => !r.IsDeleted)
            .Where(r => includeReplies || !r.ReplyTo.HasValue)
            .ToListAsync(cancellationToken);

        var reports = await _db.ReviewReports
            .AsNoTracking()
            .Where(r => reviews.Select(x => x.ReviewId).Contains(r.ReviewId))
            .ToListAsync(cancellationToken);

        var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
        var latestOrdersByUser = await _db.Orders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var rows = reviews.Select(review => MapReview(review, reports.Where(x => x.ReviewId == review.ReviewId).ToList(), latestOrdersByUser));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            rows = rows.Where(r =>
                r.user.userName.ToLowerInvariant().Contains(term) ||
                r.user.fullName.ToLowerInvariant().Contains(term) ||
                r.product.productName.ToLowerInvariant().Contains(term) ||
                r.comment.ToLowerInvariant().Contains(term));
        }

        if (rating.HasValue && rating.Value >= 1 && rating.Value <= 5)
        {
            rows = rows.Where(r => r.rating == rating.Value || (r.replyTo.HasValue && r.rating == 0));
        }

        if (status.HasValue)
        {
            if (status.Value == 1)
            {
                rows = rows.Where(r => r.isApproved);
            }
            else if (status.Value == 0)
            {
                rows = rows.Where(r => !r.isApproved);
            }
        }

        return Ok(rows.OrderByDescending(r => r.createdAt).ToList());
    }

    [HttpGet("reported")]
    public async Task<IActionResult> Reported(CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var openReports = await _db.ReviewReports
            .AsNoTracking()
            .Where(r => r.Status == 0)
            .ToListAsync(cancellationToken);

        var reviewIds = openReports.Select(x => x.ReviewId).Distinct().ToList();
        var reviews = await _db.Reviews
            .AsNoTracking()
            .Where(r => !r.IsDeleted && reviewIds.Contains(r.ReviewId))
            .ToDictionaryAsync(x => x.ReviewId, cancellationToken);

        var userIds = reviews.Values.Select(x => x.UserId).Distinct().ToList();
        var latestOrdersByUser = await _db.Orders
            .AsNoTracking()
            .Where(o => userIds.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => g.OrderByDescending(x => x.OrderDate).First())
            .ToDictionaryAsync(x => x.UserId, cancellationToken);

        var rows = openReports
            .GroupBy(x => x.ReviewId)
            .Where(g => reviews.ContainsKey(g.Key))
            .Select(g =>
            {
                var review = reviews[g.Key];
                latestOrdersByUser.TryGetValue(review.UserId, out var userOrder);
                return new
                {
                    reviewID = review.ReviewId,
                    openCount = g.Count(),
                    firstReportAt = g.Min(x => x.CreatedAt),
                    customerName = string.IsNullOrWhiteSpace(userOrder?.BuyerFullName) ? $"Khach {review.UserId}" : userOrder!.BuyerFullName,
                    productName = $"San pham #{review.ProductId}",
                    productImageFileName = (string?)null,
                    rating = review.Rating,
                    comment = review.Comment,
                    createdAt = review.CreatedAt
                };
            })
            .OrderByDescending(x => x.openCount)
            .ThenByDescending(x => x.firstReportAt)
            .ToList();

        return Ok(rows);
    }

    [HttpPost("{reviewId:int}/reports/resolve")]
    public async Task<IActionResult> ResolveReport([FromRoute] int reviewId, [FromBody] ResolveReportRequest? request, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var decision = (request?.Decision ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(decision))
        {
            return BadRequest(new { message = "Quyet dinh khong hop le." });
        }

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ReviewId == reviewId && !r.IsDeleted, cancellationToken);
        if (review is null)
        {
            return NotFound(new { message = "Khong tim thay binh luan." });
        }

        var reports = await _db.ReviewReports
            .Where(r => r.ReviewId == reviewId && r.Status == 0)
            .ToListAsync(cancellationToken);

        switch (decision)
        {
            case "dismiss":
                foreach (var report in reports)
                {
                    report.Status = 1;
                }

                await _db.SaveChangesAsync(cancellationToken);
                return Ok(new { success = true, message = "Da bo qua cac bao cao." });

            case "hide":
                review.IsApproved = false;
                review.UpdatedAt = DateTime.UtcNow;
                foreach (var report in reports)
                {
                    report.Status = 2;
                }

                await _db.SaveChangesAsync(cancellationToken);
                return Ok(new { success = true, message = "Da an binh luan va cap nhat bao cao." });

            case "delete":
                var now = DateTime.UtcNow;
                var reviewIds = await _db.Reviews
                    .Where(r => r.ReviewId == reviewId || r.ReplyTo == reviewId)
                    .Select(r => r.ReviewId)
                    .ToListAsync(cancellationToken);

                var reviewEntities = await _db.Reviews
                    .Where(r => reviewIds.Contains(r.ReviewId))
                    .ToListAsync(cancellationToken);

                foreach (var item in reviewEntities)
                {
                    item.IsDeleted = true;
                    item.DeletedAt = now;
                    item.UpdatedAt = now;
                }

                var relatedReports = await _db.ReviewReports
                    .Where(r => reviewIds.Contains(r.ReviewId) && r.Status == 0)
                    .ToListAsync(cancellationToken);

                foreach (var report in relatedReports)
                {
                    report.Status = 2;
                }

                await _db.SaveChangesAsync(cancellationToken);
                return Ok(new { success = true, message = "Da xoa binh luan va cap nhat bao cao." });

            default:
                return BadRequest(new { message = "Quyet dinh khong hop le." });
        }
    }

    [HttpDelete("{reviewId:int}")]
    public async Task<IActionResult> Delete([FromRoute] int reviewId, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ReviewId == reviewId && !r.IsDeleted, cancellationToken);
        if (review is null)
        {
            return NotFound(new { message = "Khong tim thay binh luan." });
        }

        var now = DateTime.UtcNow;
        var reviewIds = await _db.Reviews
            .Where(r => r.ReviewId == reviewId || r.ReplyTo == reviewId)
            .Select(r => r.ReviewId)
            .ToListAsync(cancellationToken);

        var reviewEntities = await _db.Reviews
            .Where(r => reviewIds.Contains(r.ReviewId))
            .ToListAsync(cancellationToken);

        foreach (var item in reviewEntities)
        {
            item.IsDeleted = true;
            item.DeletedAt = now;
            item.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da xoa binh luan." });
    }

    [HttpPost("{reviewId:int}/approve")]
    public async Task<IActionResult> Approve([FromRoute] int reviewId, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ReviewId == reviewId && !r.IsDeleted, cancellationToken);
        if (review is null)
        {
            return NotFound(new { message = "Khong tim thay danh gia." });
        }

        review.IsApproved = true;
        review.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da duyet danh gia." });
    }

    [HttpPost("{reviewId:int}/visibility/toggle")]
    public async Task<IActionResult> ToggleVisibility([FromRoute] int reviewId, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ReviewId == reviewId && !r.IsDeleted, cancellationToken);
        if (review is null)
        {
            return NotFound(new { message = "Khong tim thay binh luan." });
        }

        review.IsApproved = !review.IsApproved;
        review.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            approved = review.IsApproved,
            message = review.IsApproved ? "Da bo an binh luan." : "Da an binh luan."
        });
    }

    [HttpPost("{reviewId:int}/replies")]
    public async Task<IActionResult> Reply([FromRoute] int reviewId, [FromBody] ReplyRequest? request, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var content = (request?.Content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return BadRequest(new { message = "Noi dung phan hoi trong." });
        }

        var parent = await _db.Reviews
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReviewId == reviewId && !r.IsDeleted, cancellationToken);

        if (parent is null)
        {
            return NotFound(new { message = "Khong tim thay danh gia." });
        }

        var reply = new Review
        {
            ReplyTo = parent.ReviewId,
            UserId = request?.AdminUserId ?? 0,
            ProductId = parent.ProductId,
            Rating = 0,
            Comment = content,
            CreatedAt = DateTime.UtcNow,
            IsApproved = true,
            IsEdited = false,
            IsDeleted = false
        };

        _db.Reviews.Add(reply);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da gui phan hoi." });
    }

    [HttpPatch("{reviewId:int}")]
    public async Task<IActionResult> Update([FromRoute] int reviewId, [FromBody] UpdateReviewRequest? request, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.ReviewId == reviewId && !r.IsDeleted, cancellationToken);
        if (review is null)
        {
            return NotFound(new { message = "Khong tim thay binh luan." });
        }

        if (!string.IsNullOrWhiteSpace(request?.Comment))
        {
            review.Comment = request.Comment.Trim();
            review.IsEdited = true;
            review.UpdatedAt = DateTime.UtcNow;
        }

        if (request?.IsApproved.HasValue == true)
        {
            review.IsApproved = request.IsApproved.Value;
            review.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Cap nhat thanh cong." });
    }

    [HttpGet("{reviewId:int}/reports")]
    public async Task<IActionResult> GetReports([FromRoute] int reviewId, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        await EnsureSeedDataAsync(cancellationToken);

        const int pageSize = 5;
        if (page < 1)
        {
            page = 1;
        }

        var reports = await _db.ReviewReports
            .AsNoTracking()
            .Where(r => r.ReviewId == reviewId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var total = reports.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (page > totalPages)
        {
            page = totalPages;
        }

        var data = reports
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                reporter = r.ReporterUserId,
                reason = r.Reason,
                note = r.Note,
                createdAt = r.CreatedAt.ToString("dd/MM/yyyy HH:mm")
            })
            .ToList();

        return Ok(new
        {
            success = true,
            data,
            page,
            totalPages
        });
    }

    private async Task EnsureSeedDataAsync(CancellationToken cancellationToken)
    {
        var hasAnyReview = await _db.Reviews.AsNoTracking().AnyAsync(cancellationToken);
        if (hasAnyReview)
        {
            return;
        }

        var orders = await _db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .Take(120)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(x => x.OrderId).ToList();
        var detailLookup = await _db.OrderDetails
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .GroupBy(x => x.OrderId)
            .Select(g => new { orderId = g.Key, productId = g.OrderBy(d => d.OrderDetailId).Select(d => d.ProductId).FirstOrDefault() })
            .ToDictionaryAsync(x => x.orderId, x => x.productId, cancellationToken);

        var reviews = new List<Review>();
        foreach (var order in orders)
        {
            var generatedRating = (order.OrderId % 5) + 1;
            detailLookup.TryGetValue(order.OrderId, out var productId);

            reviews.Add(new Review
            {
                ProductId = productId > 0 ? productId : ((order.OrderId % 1000) + 1),
                UserId = order.UserId,
                Rating = generatedRating,
                Comment = $"Danh gia cho don #{order.OrderId:D6}",
                CreatedAt = order.OrderDate.AddHours(4),
                IsApproved = generatedRating >= 3,
                IsEdited = false,
                IsDeleted = false
            });
        }

        _db.Reviews.AddRange(reviews);
        await _db.SaveChangesAsync(cancellationToken);

        var seededReviews = await _db.Reviews
            .AsNoTracking()
            .OrderBy(x => x.ReviewId)
            .Take(reviews.Count)
            .ToListAsync(cancellationToken);

        var reports = new List<ReviewReport>();
        foreach (var review in seededReviews)
        {
            if (review.ReviewId % 4 != 0)
            {
                continue;
            }

            reports.Add(new ReviewReport
            {
                ReviewId = review.ReviewId,
                ReporterUserId = Math.Max(1, review.UserId + 1),
                Reason = "Noi dung khong phu hop",
                Status = 0,
                CreatedAt = review.CreatedAt.AddHours(1)
            });

            if (review.ReviewId % 8 == 0)
            {
                reports.Add(new ReviewReport
                {
                    ReviewId = review.ReviewId,
                    ReporterUserId = Math.Max(1, review.UserId + 2),
                    Reason = "Spam",
                    Note = "Kiem tra lai",
                    Status = 0,
                    CreatedAt = review.CreatedAt.AddHours(2)
                });
            }
        }

        if (reports.Count > 0)
        {
            _db.ReviewReports.AddRange(reports);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static dynamic MapReview(Review review, List<ReviewReport> reports, Dictionary<int, Order> latestOrdersByUser)
    {
        latestOrdersByUser.TryGetValue(review.UserId, out var userOrder);
        var fullName = string.IsNullOrWhiteSpace(userOrder?.BuyerFullName) ? $"Khach {review.UserId}" : userOrder!.BuyerFullName;
        var userName = string.IsNullOrWhiteSpace(userOrder?.BuyerFullName)
            ? $"user_{review.UserId}"
            : userOrder!.BuyerFullName.Replace(" ", ".").ToLowerInvariant();

        return new
        {
            reviewID = review.ReviewId,
            replyTo = review.ReplyTo,
            userID = review.UserId,
            productID = review.ProductId,
            rating = review.Rating,
            comment = review.Comment ?? string.Empty,
            createdAt = review.CreatedAt,
            isApproved = review.IsApproved,
            isEdited = review.IsEdited,
            updatedAt = review.UpdatedAt,
            user = new
            {
                userID = review.UserId,
                userName,
                fullName
            },
            product = new
            {
                productID = review.ProductId,
                productName = $"San pham #{review.ProductId}",
                imageFileName = (string?)null
            },
            reviewReports = reports.Select(r => new
            {
                reviewReportID = r.ReportId,
                reviewID = r.ReviewId,
                reporterUserID = r.ReporterUserId,
                reason = r.Reason,
                note = r.Note,
                status = r.Status,
                createdAt = r.CreatedAt
            }).ToList()
        };
    }

    public sealed class ResolveReportRequest
    {
        public string? Decision { get; set; }
    }

    public sealed class ReplyRequest
    {
        public string? Content { get; set; }

        public int? AdminUserId { get; set; }

        public string? AdminUserName { get; set; }

        public string? AdminName { get; set; }
    }

    public sealed class UpdateReviewRequest
    {
        public string? Comment { get; set; }

        public bool? IsApproved { get; set; }
    }
}
