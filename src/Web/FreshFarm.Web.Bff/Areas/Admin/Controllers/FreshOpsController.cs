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
                ViewBag.Error = await ReadApiErrorAsync(response, "Khong the tai fresh-goods operations.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<FreshOpsApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Khong doc duoc du lieu fresh-goods operations.";
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
            model.SellerOptions = payload.Filters?.Sellers?.Select(x => new FreshOpsOptionViewModel
            {
                Value = x.SellerId.ToString(),
                Text = x.Text ?? string.Empty
            }).ToList() ?? model.SellerOptions;
            model.StatusOptions = payload.Filters?.StatusOptions?.Select(x => new FreshOpsOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            }).ToList() ?? model.StatusOptions;
            model.Lots = payload.Lots?.Select(x => new FreshLotRowViewModel
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
            }).ToList() ?? new List<FreshLotRowViewModel>();
            model.Recalls = payload.Recalls?.Select(x => new FreshRecallRowViewModel
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
            }).ToList() ?? new List<FreshRecallRowViewModel>();
            model.FefoQueue = payload.FefoQueue?.Select(x => new FreshFefoRowViewModel
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
            }).ToList() ?? new List<FreshFefoRowViewModel>();

            if (!model.NewLot.SellerId.Equals(0) || !model.SellerId.HasValue)
            {
                return View(model);
            }

            model.NewLot.SellerId = model.SellerId.Value;
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai fresh-goods operations: " + ex.Message;
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
                    ? "Da tao fresh inventory lot."
                    : await ReadApiErrorAsync(response, "Khong the tao fresh inventory lot.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi tao lot: " + ex.Message;
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
                    ? "Da tao quality recall."
                    : await ReadApiErrorAsync(response, "Khong the tao quality recall.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi tao recall: " + ex.Message;
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
                    ? "Da resolve quality recall."
                    : await ReadApiErrorAsync(response, "Khong the resolve quality recall.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi resolve recall: " + ex.Message;
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
            new FreshOpsOptionViewModel { Value = "all", Text = "Tat ca" },
            new FreshOpsOptionViewModel { Value = "active", Text = "Active lot" },
            new FreshOpsOptionViewModel { Value = "held", Text = "Held" },
            new FreshOpsOptionViewModel { Value = "expired", Text = "Expired" },
            new FreshOpsOptionViewModel { Value = "recalled", Text = "Recalled" }
        ];
    }

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
