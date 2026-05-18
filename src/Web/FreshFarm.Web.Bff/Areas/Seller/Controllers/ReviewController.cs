using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using FreshFarm.Web.Bff.Areas.Seller.Models;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
public class ReviewController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ReviewController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> ManageReview()
    {
        var reviews = await GetReviewsAsync(search: null, rating: null, status: null, includeReplies: true);
        return View(reviews);
    }

    [HttpGet]
    public async Task<IActionResult> FilterReviews(string? search, int? rating, int? status)
    {
        var reviews = await GetReviewsAsync(search, rating, status, includeReplies: true);
        return View("ManageReview", reviews);
    }

    [HttpGet]
    public async Task<IActionResult> ReportedReviews()
    {
        try
        {
            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync("/api/orders/admin/reviews/reported");
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Khong the tai danh sach bao cao");
                return View(new List<SellerReportedReviewRowViewModel>());
            }

            var payload = await response.Content.ReadFromJsonAsync<List<ReportedReviewApiDto>>(JsonOptions)
                ?? new List<ReportedReviewApiDto>();

            var model = payload.Select(x => new SellerReportedReviewRowViewModel
            {
                ReviewID = x.reviewID,
                OpenCount = x.openCount,
                FirstReportAt = x.firstReportAt,
                CustomerName = x.customerName ?? "-",
                ProductName = x.productName ?? "-",
                ProductImageFileName = x.productImageFileName,
                Rating = x.rating,
                Comment = x.comment ?? string.Empty,
                CreatedAt = x.createdAt
            }).ToList();

            return View(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi: " + ex.Message;
            return View(new List<SellerReportedReviewRowViewModel>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ResolveReport(int reviewId, string decision)
    {
        try
        {
            if (reviewId <= 0)
            {
                return Json(new { success = false, message = "ID binh luan khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync($"/api/orders/admin/reviews/{reviewId}/reports/resolve", new { decision });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xu ly bao cao") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Xu ly thanh cong." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> DeleteReview(int reviewId)
    {
        try
        {
            if (reviewId <= 0)
            {
                return Json(new { success = false, message = "ID binh luan khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.DeleteAsync($"/api/orders/admin/reviews/{reviewId}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the xoa binh luan") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Da xoa binh luan." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ApproveReview(int reviewId)
    {
        try
        {
            if (reviewId <= 0)
            {
                return Json(new { success = false, message = "ID danh gia khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/reviews/{reviewId}/approve", content: null);
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the duyet danh gia") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Da duyet danh gia." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ToggleVisibility(int reviewId)
    {
        try
        {
            if (reviewId <= 0)
            {
                return Json(new { success = false, message = "ID binh luan khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsync($"/api/orders/admin/reviews/{reviewId}/visibility/toggle", content: null);
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the doi trang thai hien thi") });
            }

            var payload = await response.Content.ReadFromJsonAsync<ToggleVisibilityResponse>(JsonOptions);
            return Json(new
            {
                success = true,
                approved = payload?.approved ?? false,
                message = payload?.message ?? "Cap nhat thanh cong."
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> ReplyReview(int reviewId, string? content)
    {
        try
        {
            var normalizedContent = (content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                return Json(new { success = false, message = "Noi dung phan hoi trong." });
            }

            var adminIdObj = Session["ADMIN_ID"];
            var adminId = adminIdObj is int value ? value : 0;
            var adminName = (Session["ADMIN_NAME"] as string) ?? "Admin";

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.PostAsJsonAsync($"/api/orders/admin/reviews/{reviewId}/replies", new
            {
                content = normalizedContent,
                adminUserId = adminId,
                adminUserName = adminId > 0 ? $"ADMIN_{adminId}" : "ADMIN_SELLER",
                adminName
            });

            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the gui phan hoi") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Da gui phan hoi." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<JsonResult> UpdateReview(int id, string? comment, bool? isApproved)
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "ID binh luan khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/orders/admin/reviews/{id}")
            {
                Content = JsonContent.Create(new { comment, isApproved })
            };

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the cap nhat binh luan") });
            }

            var payload = await response.Content.ReadFromJsonAsync<BasicSuccessResponse>(JsonOptions);
            return Json(new { success = true, message = payload?.message ?? "Cap nhat thanh cong." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<JsonResult> GetReviewReports(int reviewId, int page = 1)
    {
        try
        {
            if (reviewId <= 0)
            {
                return Json(new { success = false, message = "ID binh luan khong hop le." });
            }

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/reviews/{reviewId}/reports?page={page}");
            if (!response.IsSuccessStatusCode)
            {
                return Json(new { success = false, message = await ReadApiErrorAsync(response, "Khong the tai danh sach bao cao") });
            }

            var payload = await response.Content.ReadFromJsonAsync<ReviewReportListResponse>(JsonOptions);
            return Json(payload ?? new ReviewReportListResponse
            {
                success = true,
                data = new List<ReviewReportRowDto>(),
                page = 1,
                totalPages = 1
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Loi: " + ex.Message });
        }
    }

    private async Task<List<SellerReviewViewModel>> GetReviewsAsync(string? search, int? rating, int? status, bool includeReplies)
    {
        try
        {
            var queryParts = new List<string> { $"includeReplies={includeReplies.ToString().ToLowerInvariant()}" };

            if (!string.IsNullOrWhiteSpace(search))
            {
                queryParts.Add("search=" + Uri.EscapeDataString(search.Trim()));
            }

            if (rating.HasValue)
            {
                queryParts.Add("rating=" + rating.Value);
            }

            if (status.HasValue)
            {
                queryParts.Add("status=" + status.Value);
            }

            var query = string.Join("&", queryParts);

            var client = CreateAuthorizedClient("Ordering");
            var response = await client.GetAsync($"/api/orders/admin/reviews?{query}");
            if (!response.IsSuccessStatusCode)
            {
                return new List<SellerReviewViewModel>();
            }

            var payload = await response.Content.ReadFromJsonAsync<List<ReviewApiDto>>(JsonOptions)
                ?? new List<ReviewApiDto>();

            var reviews = payload.Select(MapReview).ToList();
            await HydrateProductImagesAsync(reviews);
            await HydrateUserAvatarsAsync(reviews);

            return reviews;
        }
        catch
        {
            return new List<SellerReviewViewModel>();
        }
    }

    private HttpClient CreateAuthorizedClient(string clientName)
    {
        var client = _httpClientFactory.CreateClient(clientName);

        client.DefaultRequestHeaders.Remove("Authorization");
        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private async Task HydrateProductImagesAsync(List<SellerReviewViewModel> reviews)
    {
        var productIds = reviews
            .Select(review => review.ProductID)
            .Where(productId => productId > 0)
            .Distinct()
            .Take(200)
            .ToList();

        if (productIds.Count == 0)
        {
            return;
        }

        var imagesByProductId = await GetProductImageFileNamesAsync(productIds);
        if (imagesByProductId.Count == 0)
        {
            return;
        }

        foreach (var review in reviews)
        {
            if (review.Product is not null &&
                imagesByProductId.TryGetValue(review.ProductID, out var imageFileName) &&
                !string.IsNullOrWhiteSpace(imageFileName))
            {
                review.Product.ImageFileName = imageFileName;
            }
        }
    }

    private async Task<Dictionary<int, string>> GetProductImageFileNamesAsync(IReadOnlyCollection<int> productIds)
    {
        try
        {
            var client = CreateAuthorizedClient("Catalog");
            var query = string.Join("&", productIds.Select(productId => $"productIds={productId}"));
            var response = await client.GetAsync($"/api/products?{query}&limit={Math.Min(productIds.Count, 200)}");
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<int, string>();
            }

            var products = await response.Content.ReadFromJsonAsync<List<CatalogProductImageApiDto>>(JsonOptions)
                ?? new List<CatalogProductImageApiDto>();

            return products
                .Where(product => product.ProductId > 0)
                .Select(product => new
                {
                    product.ProductId,
                    ImageFileName = ProductImagePaths.NormalizeStoredFileName(product.ImageFileName)
                })
                .Where(product => !string.IsNullOrWhiteSpace(product.ImageFileName))
                .GroupBy(product => product.ProductId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().ImageFileName!);
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private async Task HydrateUserAvatarsAsync(List<SellerReviewViewModel> reviews)
    {
        var userIds = reviews
            .Select(review => review.UserID)
            .Where(userId => userId > 0)
            .Distinct()
            .ToHashSet();

        if (userIds.Count == 0)
        {
            return;
        }

        var avatarsByUserId = await GetCustomerAvatarUrlsAsync(userIds);
        if (avatarsByUserId.Count == 0)
        {
            return;
        }

        foreach (var review in reviews)
        {
            if (review.User is not null &&
                avatarsByUserId.TryGetValue(review.UserID, out var avatarUrl))
            {
                review.User.AvatarUrl = avatarUrl;
            }
        }
    }

    private async Task<Dictionary<int, string>> GetCustomerAvatarUrlsAsync(IReadOnlySet<int> userIds)
    {
        try
        {
            var client = CreateAuthorizedClient("Identity");
            var query = string.Join("&", userIds.Select(userId => $"userIds={userId}"));
            var response = await client.GetAsync($"/auth/admin/customers?{query}&take={Math.Min(userIds.Count, 5000)}");
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<int, string>();
            }

            var customers = await response.Content.ReadFromJsonAsync<List<CustomerAvatarApiDto>>(JsonOptions)
                ?? new List<CustomerAvatarApiDto>();

            return customers
                .Where(customer => userIds.Contains(customer.userId))
                .Select(customer => new
                {
                    customer.userId,
                    AvatarUrl = AvatarImagePaths.ResolveRequestPath(customer.avatar)
                })
                .Where(customer => !string.IsNullOrWhiteSpace(customer.AvatarUrl))
                .GroupBy(customer => customer.userId)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().AvatarUrl!);
        }
        catch
        {
            return new Dictionary<int, string>();
        }
    }

    private static SellerReviewViewModel MapReview(ReviewApiDto dto)
    {
        return new SellerReviewViewModel
        {
            ReviewID = dto.reviewID,
            ReplyTo = dto.replyTo,
            UserID = dto.userID,
            ProductID = dto.productID,
            Rating = dto.rating,
            Comment = dto.comment ?? string.Empty,
            CreatedAt = dto.createdAt,
            IsApproved = dto.isApproved,
            IsEdited = dto.isEdited,
            UpdatedAt = dto.updatedAt,
            User = dto.user is null
                ? null
                : new SellerReviewUserViewModel
                {
                    UserID = dto.user.userID,
                    UserName = dto.user.userName ?? string.Empty,
                    FullName = dto.user.fullName,
                    AvatarUrl = AvatarImagePaths.ResolveRequestPath(dto.user.avatar)
                },
            Product = dto.product is null
                ? null
                : new SellerReviewProductViewModel
                {
                    ProductID = dto.product.productID,
                    ProductName = dto.product.productName ?? string.Empty,
                    ImageFileName = dto.product.imageFileName
                },
            ReviewReports = dto.reviewReports?.Select(r => new SellerReviewReportViewModel
            {
                ReviewReportID = r.reviewReportID,
                ReviewID = r.reviewID,
                ReporterUserID = r.reporterUserID,
                Reason = r.reason ?? string.Empty,
                Note = r.note,
                Status = r.status,
                CreatedAt = r.createdAt
            }).ToList() ?? new List<SellerReviewReportViewModel>()
        };
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
                {
                    return messageProp.GetString() ?? fallback;
                }

                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    return detailProp.GetString() ?? fallback;
                }

                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    return titleProp.GetString() ?? fallback;
                }
            }

            return json;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class ReviewApiDto
    {
        public int reviewID { get; set; }

        public int? replyTo { get; set; }

        public int userID { get; set; }

        public int productID { get; set; }

        public int rating { get; set; }

        public string? comment { get; set; }

        public DateTime createdAt { get; set; }

        public bool isApproved { get; set; }

        public bool isEdited { get; set; }

        public DateTime? updatedAt { get; set; }

        public ReviewUserApiDto? user { get; set; }

        public ReviewProductApiDto? product { get; set; }

        public List<ReviewReportApiDto>? reviewReports { get; set; }
    }

    private sealed class ReviewUserApiDto
    {
        public int userID { get; set; }

        public string? userName { get; set; }

        public string? fullName { get; set; }

        public string? avatar { get; set; }
    }

    private sealed class ReviewProductApiDto
    {
        public int productID { get; set; }

        public string? productName { get; set; }

        public string? imageFileName { get; set; }
    }

    private sealed class CatalogProductImageApiDto
    {
        public int ProductId { get; set; }

        public string? ImageFileName { get; set; }
    }

    private sealed class CustomerAvatarApiDto
    {
        public int userId { get; set; }

        public string? avatar { get; set; }
    }

    private sealed class ReviewReportApiDto
    {
        public int reviewReportID { get; set; }

        public int reviewID { get; set; }

        public int reporterUserID { get; set; }

        public string? reason { get; set; }

        public string? note { get; set; }

        public int status { get; set; }

        public DateTime createdAt { get; set; }
    }

    private sealed class ReportedReviewApiDto
    {
        public int reviewID { get; set; }

        public int openCount { get; set; }

        public DateTime firstReportAt { get; set; }

        public string? customerName { get; set; }

        public string? productName { get; set; }

        public string? productImageFileName { get; set; }

        public int rating { get; set; }

        public string? comment { get; set; }

        public DateTime createdAt { get; set; }
    }

    private sealed class ReviewReportListResponse
    {
        public bool success { get; set; }

        public List<ReviewReportRowDto> data { get; set; } = new();

        public int page { get; set; }

        public int totalPages { get; set; }
    }

    private sealed class ReviewReportRowDto
    {
        public int reporter { get; set; }

        public string? reason { get; set; }

        public string? note { get; set; }

        public string? createdAt { get; set; }
    }

    private sealed class BasicSuccessResponse
    {
        public bool success { get; set; }

        public string? message { get; set; }
    }

    private sealed class ToggleVisibilityResponse
    {
        public bool success { get; set; }

        public bool approved { get; set; }

        public string? message { get; set; }
    }
}
