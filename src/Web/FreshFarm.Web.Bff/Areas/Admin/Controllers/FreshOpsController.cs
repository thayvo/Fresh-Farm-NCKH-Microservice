using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Admin")]
public sealed class FreshOpsController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FreshOpsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, int? sellerId = null, string? status = null)
    {
        var model = new FreshOpsPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            SellerId = sellerId > 0 ? sellerId : null,
            Status = NormalizeStatus(status)
        };
        SeedFallbackOptions(model);

        try
        {
            var client = CreateCatalogClient();
            var response = await client.GetAsync(BuildEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải trung tâm vận hành hàng tươi.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<FreshOpsApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu trung tâm vận hành hàng tươi.";
                return View(model);
            }

            model.Query = payload.Filters?.Q ?? model.Query;
            model.SellerId = payload.Filters?.SellerId > 0 ? payload.Filters.SellerId : model.SellerId;
            model.Status = payload.Filters?.Status ?? model.Status;
            model.Stats = new FreshOpsStatsViewModel
            {
                TotalLots = payload.Stats?.TotalLots ?? 0,
                ExpiringSoonLots = payload.Stats?.ExpiringSoonLots ?? 0,
                ExpiredLots = payload.Stats?.ExpiredLots ?? 0,
                OpenRecalls = payload.Stats?.OpenRecalls ?? 0,
                TraceCoverage = payload.Stats?.TraceCoverage ?? 0
            };
            model.SellerOptions = DeduplicateSellerOptions(payload.Filters?.Sellers).Select(x => new FreshOpsOptionViewModel
            {
                Value = x.SellerId.ToString(),
                Text = x.Text ?? string.Empty
            }).ToList();
            model.StatusOptions = DeduplicateStatusOptions(payload.Filters?.StatusOptions).Select(x => new FreshOpsOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            }).ToList();
            model.Lots = DeduplicateLots(payload.Lots).Select(x => new FreshLotRowViewModel
            {
                FreshInventoryLotId = x.FreshInventoryLotId,
                ProductId = x.ProductId,
                ProductName = x.ProductName ?? string.Empty,
                Sku = x.Sku ?? string.Empty,
                SellerId = x.SellerId,
                LotCode = x.LotCode ?? string.Empty,
                TraceCode = x.TraceCode,
                FarmName = x.FarmName,
                OriginRegion = x.OriginRegion,
                HarvestedAt = x.HarvestedAt,
                PackedAt = x.PackedAt,
                ReceivedAt = x.ReceivedAt,
                ExpiresAt = x.ExpiresAt,
                InitialQuantity = x.InitialQuantity,
                RemainingQuantity = x.RemainingQuantity,
                UnitCost = x.UnitCost,
                Status = x.Status ?? string.Empty,
                QualityStatus = x.QualityStatus ?? string.Empty,
                Notes = x.Notes,
                DaysToExpiry = x.DaysToExpiry
            }).ToList();
            model.Recalls = DeduplicateRecalls(payload.Recalls).Select(x => new FreshRecallRowViewModel
            {
                FreshQualityRecallId = x.FreshQualityRecallId,
                RecallCode = x.RecallCode ?? string.Empty,
                ProductId = x.ProductId,
                ProductName = x.ProductName,
                FreshInventoryLotId = x.FreshInventoryLotId,
                LotCode = x.LotCode,
                SellerId = x.SellerId,
                RecallType = x.RecallType ?? string.Empty,
                Severity = x.Severity ?? string.Empty,
                Status = x.Status ?? string.Empty,
                Title = x.Title ?? string.Empty,
                Reason = x.Reason,
                ActionRequired = x.ActionRequired,
                StartedAt = x.StartedAt,
                ResolvedAt = x.ResolvedAt
            }).ToList();
            model.FefoQueue = DeduplicateFefoQueue(payload.FefoQueue).Select(x => new FreshFefoRowViewModel
            {
                FreshInventoryLotId = x.FreshInventoryLotId,
                ProductName = x.ProductName ?? string.Empty,
                Sku = x.Sku ?? string.Empty,
                SellerId = x.SellerId,
                LotCode = x.LotCode ?? string.Empty,
                RemainingQuantity = x.RemainingQuantity,
                ExpiresAt = x.ExpiresAt,
                DaysToExpiry = x.DaysToExpiry,
                QualityStatus = x.QualityStatus ?? string.Empty
            }).ToList();

            if (!model.NewLot.SellerId.Equals(0) || !model.SellerId.HasValue)
            {
                return View(model);
            }

            model.NewLot.SellerId = model.SellerId.Value;
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải trung tâm vận hành hàng tươi: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLot(FreshLotEditorInput input, string? q = null, int? sellerId = null, string? status = null)
    {
        try
        {
            var client = CreateCatalogClient();
            var response = await client.PostAsJsonAsync("/api/catalog/admin/fresh-ops/lots", input);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? "Đã tạo lô hàng tươi."
                    : await ReadApiErrorAsync(response, "Không thể tạo lô hàng tươi.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi tạo lô hàng: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { q, sellerId = sellerId > 0 ? sellerId : input.SellerId, status = NormalizeStatus(status) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRecall(FreshRecallEditorInput input, string? q = null, int? sellerId = null, string? status = null)
    {
        try
        {
            var client = CreateCatalogClient();
            var response = await client.PostAsJsonAsync("/api/catalog/admin/fresh-ops/recalls", input);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? "Đã tạo ca thu hồi chất lượng."
                    : await ReadApiErrorAsync(response, "Không thể tạo ca thu hồi chất lượng.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi tạo ca thu hồi: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { q, sellerId = sellerId > 0 ? sellerId : input.SellerId, status = NormalizeStatus(status) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveRecall(int id, string? q = null, int? sellerId = null, string? status = null)
    {
        try
        {
            var client = CreateCatalogClient();
            var response = await client.PostAsync($"/api/catalog/admin/fresh-ops/recalls/{id}/resolve", null);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                response.IsSuccessStatusCode
                    ? "Đã đánh dấu xử lý xong ca thu hồi."
                    : await ReadApiErrorAsync(response, "Không thể cập nhật ca thu hồi chất lượng.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi cập nhật ca thu hồi: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { q, sellerId, status = NormalizeStatus(status) });
    }

    private HttpClient CreateCatalogClient()
    {
        var client = _httpClientFactory.CreateClient("Catalog");
        client.DefaultRequestHeaders.Remove("Authorization");
        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string BuildEndpoint(FreshOpsPageViewModel model)
    {
        var parts = new List<string> { "status=" + Uri.EscapeDataString(model.Status) };
        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            parts.Add("q=" + Uri.EscapeDataString(model.Query));
        }

        if (model.SellerId.HasValue && model.SellerId.Value > 0)
        {
            parts.Add("sellerId=" + model.SellerId.Value);
        }

        return "/api/catalog/admin/fresh-ops/center?" + string.Join("&", parts);
    }

    private static string NormalizeStatus(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

    private static void SeedFallbackOptions(FreshOpsPageViewModel model)
    {
        model.StatusOptions =
        [
            new FreshOpsOptionViewModel { Value = "all", Text = "Tất cả" },
            new FreshOpsOptionViewModel { Value = "active", Text = "Lô đang hoạt động" },
            new FreshOpsOptionViewModel { Value = "held", Text = "Lô đang giữ" },
            new FreshOpsOptionViewModel { Value = "expired", Text = "Lô hết hạn" },
            new FreshOpsOptionViewModel { Value = "recalled", Text = "Lô bị thu hồi" }
        ];
    }

    private static List<FreshSellerOptionApiModel> DeduplicateSellerOptions(IEnumerable<FreshSellerOptionApiModel>? sellers)
    {
        return (sellers ?? [])
            .Where(seller => seller.SellerId > 0)
            .GroupBy(seller => seller.SellerId)
            .Select(group => group
                .OrderByDescending(seller => HasMeaningfulValue(seller.Text))
                .ThenByDescending(CalculateSellerSignalLength)
                .First())
            .ToList();
    }

    private static List<FreshStatusOptionApiModel> DeduplicateStatusOptions(IEnumerable<FreshStatusOptionApiModel>? statuses)
    {
        return (statuses ?? [])
            .Where(status => HasMeaningfulValue(status.Value))
            .GroupBy(status => status.Value!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(status => HasMeaningfulValue(status.Text))
                .ThenByDescending(CalculateStatusSignalLength)
                .First())
            .ToList();
    }

    private static List<FreshLotApiModel> DeduplicateLots(IEnumerable<FreshLotApiModel>? lots)
    {
        return (lots ?? [])
            .Where(lot => lot.FreshInventoryLotId > 0)
            .GroupBy(lot => lot.FreshInventoryLotId)
            .Select(group => group
                .OrderByDescending(CalculateLotScore)
                .ThenByDescending(CalculateLotSignalLength)
                .ThenByDescending(lot => lot.ReceivedAt)
                .ThenByDescending(lot => lot.ExpiresAt ?? DateTime.MinValue)
                .First())
            .ToList();
    }

    private static List<FreshRecallApiModel> DeduplicateRecalls(IEnumerable<FreshRecallApiModel>? recalls)
    {
        return (recalls ?? [])
            .Where(recall => recall.FreshQualityRecallId > 0)
            .GroupBy(recall => recall.FreshQualityRecallId)
            .Select(group => group
                .OrderByDescending(CalculateRecallScore)
                .ThenByDescending(CalculateRecallSignalLength)
                .ThenByDescending(recall => recall.ResolvedAt ?? recall.StartedAt)
                .First())
            .ToList();
    }

    private static List<FreshFefoApiModel> DeduplicateFefoQueue(IEnumerable<FreshFefoApiModel>? queue)
    {
        return (queue ?? [])
            .Where(item => item.FreshInventoryLotId > 0)
            .GroupBy(item => item.FreshInventoryLotId)
            .Select(group => group
                .OrderByDescending(CalculateFefoScore)
                .ThenByDescending(CalculateFefoSignalLength)
                .ThenByDescending(item => item.ExpiresAt)
                .First())
            .ToList();
    }

    private static int CalculateSellerSignalLength(FreshSellerOptionApiModel seller)
        => seller.Text?.Length ?? 0;

    private static int CalculateStatusSignalLength(FreshStatusOptionApiModel status)
        => (status.Text?.Length ?? 0) + (status.Value?.Length ?? 0);

    private static int CalculateLotScore(FreshLotApiModel lot)
    {
        var score = 0;

        score += HasMeaningfulValue(lot.ProductName) ? 2 : 0;
        score += HasMeaningfulValue(lot.Sku) ? 2 : 0;
        score += HasMeaningfulValue(lot.LotCode) ? 2 : 0;
        score += HasMeaningfulValue(lot.TraceCode) ? 1 : 0;
        score += HasMeaningfulValue(lot.FarmName) ? 1 : 0;
        score += HasMeaningfulValue(lot.OriginRegion) ? 1 : 0;
        score += HasMeaningfulValue(lot.Status) ? 1 : 0;
        score += HasMeaningfulValue(lot.QualityStatus) ? 1 : 0;
        score += HasMeaningfulValue(lot.Notes) ? 1 : 0;
        score += lot.ProductId > 0 ? 1 : 0;
        score += lot.SellerId > 0 ? 1 : 0;
        score += lot.InitialQuantity > 0 ? 1 : 0;
        score += lot.RemainingQuantity > 0 ? 1 : 0;
        score += lot.UnitCost.HasValue && lot.UnitCost.Value > 0 ? 1 : 0;
        score += lot.DaysToExpiry.HasValue ? 1 : 0;

        return score;
    }

    private static int CalculateLotSignalLength(FreshLotApiModel lot)
    {
        var values = new[]
        {
            lot.ProductName,
            lot.Sku,
            lot.LotCode,
            lot.TraceCode,
            lot.FarmName,
            lot.OriginRegion,
            lot.Status,
            lot.QualityStatus,
            lot.Notes
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static int CalculateRecallScore(FreshRecallApiModel recall)
    {
        var score = 0;

        score += HasMeaningfulValue(recall.RecallCode) ? 2 : 0;
        score += recall.ProductId.HasValue && recall.ProductId.Value > 0 ? 1 : 0;
        score += HasMeaningfulValue(recall.ProductName) ? 2 : 0;
        score += recall.FreshInventoryLotId.HasValue && recall.FreshInventoryLotId.Value > 0 ? 1 : 0;
        score += HasMeaningfulValue(recall.LotCode) ? 1 : 0;
        score += recall.SellerId.HasValue && recall.SellerId.Value > 0 ? 1 : 0;
        score += HasMeaningfulValue(recall.RecallType) ? 1 : 0;
        score += HasMeaningfulValue(recall.Severity) ? 1 : 0;
        score += HasMeaningfulValue(recall.Status) ? 1 : 0;
        score += HasMeaningfulValue(recall.Title) ? 2 : 0;
        score += HasMeaningfulValue(recall.Reason) ? 1 : 0;
        score += HasMeaningfulValue(recall.ActionRequired) ? 1 : 0;

        return score;
    }

    private static int CalculateRecallSignalLength(FreshRecallApiModel recall)
    {
        var values = new[]
        {
            recall.RecallCode,
            recall.ProductName,
            recall.LotCode,
            recall.RecallType,
            recall.Severity,
            recall.Status,
            recall.Title,
            recall.Reason,
            recall.ActionRequired
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static int CalculateFefoScore(FreshFefoApiModel item)
    {
        var score = 0;

        score += HasMeaningfulValue(item.ProductName) ? 2 : 0;
        score += HasMeaningfulValue(item.Sku) ? 2 : 0;
        score += item.SellerId > 0 ? 1 : 0;
        score += HasMeaningfulValue(item.LotCode) ? 2 : 0;
        score += item.RemainingQuantity > 0 ? 1 : 0;
        score += item.DaysToExpiry != 0 ? 1 : 0;
        score += HasMeaningfulValue(item.QualityStatus) ? 1 : 0;

        return score;
    }

    private static int CalculateFefoSignalLength(FreshFefoApiModel item)
    {
        var values = new[]
        {
            item.ProductName,
            item.Sku,
            item.LotCode,
            item.QualityStatus
        };

        return values.Sum(value => value?.Length ?? 0);
    }

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private sealed class FreshOpsApiResponse
    {
        public FreshOpsStatsApiModel? Stats { get; set; }

        public FreshOpsFiltersApiModel? Filters { get; set; }

        public List<FreshLotApiModel>? Lots { get; set; }

        public List<FreshRecallApiModel>? Recalls { get; set; }

        public List<FreshFefoApiModel>? FefoQueue { get; set; }
    }

    private sealed class FreshOpsStatsApiModel
    {
        public int TotalLots { get; set; }
        public int ExpiringSoonLots { get; set; }
        public int ExpiredLots { get; set; }
        public int OpenRecalls { get; set; }
        public int TraceCoverage { get; set; }
    }

    private sealed class FreshOpsFiltersApiModel
    {
        public string? Q { get; set; }
        public int? SellerId { get; set; }
        public string? Status { get; set; }
        public List<FreshStatusOptionApiModel>? StatusOptions { get; set; }
        public List<FreshSellerOptionApiModel>? Sellers { get; set; }
    }

    private sealed class FreshStatusOptionApiModel
    {
        public string? Value { get; set; }
        public string? Text { get; set; }
    }

    private sealed class FreshSellerOptionApiModel
    {
        public int SellerId { get; set; }
        public string? Text { get; set; }
    }

    private sealed class FreshLotApiModel
    {
        public int FreshInventoryLotId { get; set; }
        public int ProductId { get; set; }
        public string? ProductName { get; set; }
        public string? Sku { get; set; }
        public int SellerId { get; set; }
        public string? LotCode { get; set; }
        public string? TraceCode { get; set; }
        public string? FarmName { get; set; }
        public string? OriginRegion { get; set; }
        public DateTime? HarvestedAt { get; set; }
        public DateTime? PackedAt { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int InitialQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal? UnitCost { get; set; }
        public string? Status { get; set; }
        public string? QualityStatus { get; set; }
        public string? Notes { get; set; }
        public int? DaysToExpiry { get; set; }
    }

    private sealed class FreshRecallApiModel
    {
        public int FreshQualityRecallId { get; set; }
        public string? RecallCode { get; set; }
        public int? ProductId { get; set; }
        public string? ProductName { get; set; }
        public int? FreshInventoryLotId { get; set; }
        public string? LotCode { get; set; }
        public int? SellerId { get; set; }
        public string? RecallType { get; set; }
        public string? Severity { get; set; }
        public string? Status { get; set; }
        public string? Title { get; set; }
        public string? Reason { get; set; }
        public string? ActionRequired { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }

    private sealed class FreshFefoApiModel
    {
        public int FreshInventoryLotId { get; set; }
        public string? ProductName { get; set; }
        public string? Sku { get; set; }
        public int SellerId { get; set; }
        public string? LotCode { get; set; }
        public int RemainingQuantity { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int DaysToExpiry { get; set; }
        public string? QualityStatus { get; set; }
    }
}
