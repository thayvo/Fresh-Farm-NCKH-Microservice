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
public sealed class CampaignController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CampaignController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q = null, string? status = null, string? type = null, int? campaignId = null)
    {
        var model = new CampaignCenterPageViewModel
        {
            Query = q?.Trim() ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant(),
            Type = string.IsNullOrWhiteSpace(type) ? "all" : type.Trim().ToLowerInvariant(),
            SelectedCampaignId = campaignId
        };
        SeedFallbackOptions(model);

        try
        {
            var client = CreateOrderingClient();
            var overviewTask = client.GetAsync("/api/orders/admin/campaigns/overview");
            var listTask = client.GetAsync(BuildListEndpoint(model));
            var adsOverviewTask = client.GetAsync($"/api/orders/admin/campaigns/ads/overview?status=all&q={Uri.EscapeDataString(model.AdsTopupEditor.SellerId > 0 ? model.AdsTopupEditor.SellerId.ToString() : string.Empty)}");
            await Task.WhenAll(overviewTask, listTask, adsOverviewTask);

            var overviewResponse = await overviewTask;
            if (overviewResponse.IsSuccessStatusCode)
            {
                var overview = await overviewResponse.Content.ReadFromJsonAsync<CampaignOverviewApiModel>(JsonOptions);
                if (overview is not null)
                {
                    model.Overview = MapOverview(overview);
                }
            }
            else
            {
                ViewBag.Error = await ReadApiErrorAsync(overviewResponse, "Khong the tai tong quan campaign.");
            }

            var listResponse = await listTask;
            if (!listResponse.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(listResponse, "Khong the tai danh sach campaign.");
                return View(model);
            }

            var payload = await listResponse.Content.ReadFromJsonAsync<CampaignListApiResponse>(JsonOptions);
            if (payload is not null)
            {
                model.Query = payload.Filters?.Q ?? model.Query;
                model.Status = payload.Filters?.Status ?? model.Status;
                model.Type = payload.Filters?.Type ?? model.Type;
                model.StatusOptions = payload.StatusOptions?.Select(MapOption).ToList() ?? new List<CampaignOptionViewModel>();
                model.TypeOptions = payload.TypeOptions?.Select(MapOption).ToList() ?? new List<CampaignOptionViewModel>();
                model.Campaigns = payload.Rows?.Select(MapCampaignListItem).ToList() ?? new List<CampaignListItemViewModel>();
            }

            var adsOverviewResponse = await adsOverviewTask;
            if (adsOverviewResponse.IsSuccessStatusCode)
            {
                var adsPayload = await adsOverviewResponse.Content.ReadFromJsonAsync<AdsOverviewApiResponse>(JsonOptions);
                if (adsPayload is not null)
                {
                    MapAdsOverview(model, adsPayload);
                }
            }
            else
            {
                ViewBag.AdsError = await ReadApiErrorAsync(adsOverviewResponse, "Khong the tai ads wallet overview.");
            }

            if (campaignId.HasValue)
            {
                await LoadSelectedCampaignAsync(client, model, campaignId.Value);
            }
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Loi khi tai campaign center: " + ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TopupAdsWallet(AdsTopupInputModel input)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/campaigns/ads/topups", input);
            TempData[response.IsSuccessStatusCode ? "Success" : "Error"] = response.IsSuccessStatusCode
                ? "Da nap ngan sach ads wallet."
                : await ReadApiErrorAsync(response, "Khong the nap ngan sach ads wallet.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi nap ads wallet: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SpendAdsWallet(AdsSpendInputModel input)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/campaigns/ads/spends", input);
            TempData[response.IsSuccessStatusCode ? "Success" : "Error"] = response.IsSuccessStatusCode
                ? "Da ghi nhan spend ads wallet."
                : await ReadApiErrorAsync(response, "Khong the ghi nhan spend ads wallet.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi ghi nhan spend ads wallet: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { campaignId = input.AdsCampaignId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAdsCampaign(AdsCampaignInputModel input)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/campaigns/ads/campaigns", input);
            TempData[response.IsSuccessStatusCode ? "Success" : "Error"] = response.IsSuccessStatusCode
                ? "Da tao ads campaign MVP."
                : await ReadApiErrorAsync(response, "Khong the tao ads campaign.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi tao ads campaign: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { campaignId = input.CampaignId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CampaignUpsertInputModel input)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Du lieu campaign khong hop le.";
            return RedirectToAction(nameof(Index), new { campaignId = input.CampaignId });
        }

        try
        {
            var client = CreateOrderingClient();
            HttpResponseMessage response;
            if (input.CampaignId.HasValue && input.CampaignId.Value > 0)
            {
                response = await client.PutAsJsonAsync($"/api/orders/admin/campaigns/{input.CampaignId.Value}", input);
            }
            else
            {
                response = await client.PostAsJsonAsync("/api/orders/admin/campaigns", input);
            }

            if (!response.IsSuccessStatusCode)
            {
                TempData["Error"] = await ReadApiErrorAsync(response, "Khong the luu campaign.");
                return RedirectToAction(nameof(Index), new { campaignId = input.CampaignId });
            }

            TempData["Success"] = input.CampaignId.HasValue ? "Cap nhat campaign thanh cong." : "Tao campaign thanh cong.";
            return RedirectToAction(nameof(Index), new { campaignId = input.CampaignId });
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi luu campaign: " + ex.Message;
            return RedirectToAction(nameof(Index), new { campaignId = input.CampaignId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRegistration(int id)
    {
        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsync($"/api/orders/admin/campaigns/{id}/toggle-registration", null);
            TempData[response.IsSuccessStatusCode ? "Success" : "Error"] = response.IsSuccessStatusCode
                ? "Cap nhat trang thai dang ky thanh cong."
                : await ReadApiErrorAsync(response, "Khong the doi trang thai dang ky.");
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Loi khi doi trang thai dang ky: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { campaignId = id });
    }

    private async Task LoadSelectedCampaignAsync(HttpClient client, CampaignCenterPageViewModel model, int campaignId)
    {
        var response = await client.GetAsync($"/api/orders/admin/campaigns/{campaignId}");
        if (!response.IsSuccessStatusCode)
        {
            TempData["Error"] = await ReadApiErrorAsync(response, "Khong the tai chi tiet campaign.");
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<CampaignDetailApiModel>(JsonOptions);
        if (payload is null)
        {
            return;
        }

        model.SelectedCampaign = new CampaignDetailViewModel
        {
            CampaignId = payload.CampaignId,
            Name = payload.Name ?? string.Empty,
            CampaignType = payload.CampaignType ?? string.Empty,
            Description = payload.Description,
            Status = payload.Status ?? string.Empty,
            BudgetAmount = payload.BudgetAmount,
            IsFeatured = payload.IsFeatured,
            VoucherCouponId = payload.VoucherCouponId,
            VoucherCode = payload.VoucherCode,
            RegistrationStartAt = payload.RegistrationStartAt,
            RegistrationEndAt = payload.RegistrationEndAt,
            StartAt = payload.StartAt,
            EndAt = payload.EndAt,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.UpdatedAt,
            ApprovedAt = payload.ApprovedAt,
            Participations = payload.Participations?.Select(x => new CampaignParticipationViewModel
            {
                ParticipationId = x.ParticipationId,
                SellerId = x.SellerId,
                Status = x.Status ?? string.Empty,
                Notes = x.Notes,
                DiscountPercent = x.DiscountPercent,
                RequestedSlots = x.RequestedSlots,
                ApprovedSlots = x.ApprovedSlots,
                RequestedAt = x.RequestedAt,
                ReviewedAt = x.ReviewedAt,
                ReviewedBy = x.ReviewedBy
            }).ToList() ?? new List<CampaignParticipationViewModel>(),
            Slots = payload.Slots?.Select(x => new CampaignSlotViewModel
            {
                SlotId = x.SlotId,
                ParticipationId = x.ParticipationId,
                SellerId = x.SellerId,
                ProductId = x.ProductId,
                Status = x.Status ?? string.Empty,
                FlashSalePrice = x.FlashSalePrice,
                InventoryLimit = x.InventoryLimit,
                CreatedAt = x.CreatedAt,
                ApprovedAt = x.ApprovedAt
            }).ToList() ?? new List<CampaignSlotViewModel>()
        };

        model.Editor = new CampaignUpsertInputModel
        {
            CampaignId = payload.CampaignId,
            Name = payload.Name ?? string.Empty,
            CampaignType = payload.CampaignType ?? "flash_sale",
            Status = payload.Status ?? "draft",
            Description = payload.Description,
            RegistrationStartAt = payload.RegistrationStartAt,
            RegistrationEndAt = payload.RegistrationEndAt,
            StartAt = payload.StartAt,
            EndAt = payload.EndAt,
            BudgetAmount = payload.BudgetAmount,
            IsFeatured = payload.IsFeatured,
            VoucherCouponId = payload.VoucherCouponId
        };
    }

    private HttpClient CreateOrderingClient()
    {
        var client = _httpClientFactory.CreateClient("Ordering");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string BuildListEndpoint(CampaignCenterPageViewModel model)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            query.Add("q=" + Uri.EscapeDataString(model.Query));
        }

        if (!string.IsNullOrWhiteSpace(model.Status))
        {
            query.Add("status=" + Uri.EscapeDataString(model.Status));
        }

        if (!string.IsNullOrWhiteSpace(model.Type))
        {
            query.Add("type=" + Uri.EscapeDataString(model.Type));
        }

        return query.Count == 0 ? "/api/orders/admin/campaigns" : "/api/orders/admin/campaigns?" + string.Join("&", query);
    }

    private static CampaignOverviewViewModel MapOverview(CampaignOverviewApiModel payload)
        => new()
        {
            TotalCampaigns = payload.TotalCampaigns,
            RunningCampaigns = payload.RunningCampaigns,
            RegistrationOpenCampaigns = payload.RegistrationOpenCampaigns,
            ScheduledCampaigns = payload.ScheduledCampaigns,
            TotalParticipations = payload.TotalParticipations,
            ApprovedParticipations = payload.ApprovedParticipations,
            PendingParticipations = payload.PendingParticipations,
            TotalSlots = payload.TotalSlots,
            ApprovedSlots = payload.ApprovedSlots,
            LiveSlots = payload.LiveSlots,
            TotalBudget = payload.TotalBudget,
            Upcoming = payload.Upcoming?.Select(x => new CampaignUpcomingViewModel
            {
                CampaignId = x.CampaignId,
                Name = x.Name ?? string.Empty,
                CampaignType = x.CampaignType ?? string.Empty,
                Status = x.Status ?? string.Empty,
                StartAt = x.StartAt,
                EndAt = x.EndAt,
                RegistrationEndAt = x.RegistrationEndAt
            }).ToList() ?? new List<CampaignUpcomingViewModel>()
        };

    private static CampaignOptionViewModel MapOption(CampaignOptionApiModel option)
        => new()
        {
            Value = option.Value ?? string.Empty,
            Text = TranslateCampaignOption(option.Value, option.Text)
        };

    private static string TranslateCampaignOption(string? value, string? text)
        => (value ?? string.Empty).ToLowerInvariant() switch
        {
            "all" => "Tất cả",
            "draft" => "Nháp",
            "registration_open" => "Mở đăng ký",
            "registration_closed" => "Đóng đăng ký",
            "scheduled" => "Đã lên lịch",
            "running" => "Đang chạy",
            "completed" => "Hoàn tất",
            "suspended" => "Tạm ngưng",
            "active" => "Hoạt động",
            "paused" => "Tạm dừng",
            "flash_sale" => "Flash sale",
            "voucher_boost" => "Đẩy voucher",
            "seasonal" => "Theo mùa",
            "livestream" => "Livestream",
            _ => text ?? string.Empty
        };

    private static CampaignListItemViewModel MapCampaignListItem(CampaignListItemApiModel item)
        => new()
        {
            CampaignId = item.CampaignId,
            Name = item.Name ?? string.Empty,
            CampaignType = item.CampaignType ?? string.Empty,
            Description = item.Description,
            Status = item.Status ?? string.Empty,
            BudgetAmount = item.BudgetAmount,
            IsFeatured = item.IsFeatured,
            VoucherCouponId = item.VoucherCouponId,
            VoucherCode = item.VoucherCode,
            RegistrationStartAt = item.RegistrationStartAt,
            RegistrationEndAt = item.RegistrationEndAt,
            StartAt = item.StartAt,
            EndAt = item.EndAt,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            ParticipationCount = item.ParticipationCount,
            ApprovedParticipationCount = item.ApprovedParticipationCount,
            ProductSlotCount = item.ProductSlotCount,
            ApprovedSlotCount = item.ApprovedSlotCount
        };

    private static void SeedFallbackOptions(CampaignCenterPageViewModel model)
    {
        model.StatusOptions =
        [
            new CampaignOptionViewModel { Value = "all", Text = "Tất cả" },
            new CampaignOptionViewModel { Value = "draft", Text = "Nháp" },
            new CampaignOptionViewModel { Value = "registration_open", Text = "Mở đăng ký" },
            new CampaignOptionViewModel { Value = "registration_closed", Text = "Đóng đăng ký" },
            new CampaignOptionViewModel { Value = "scheduled", Text = "Đã lên lịch" },
            new CampaignOptionViewModel { Value = "running", Text = "Đang chạy" },
            new CampaignOptionViewModel { Value = "completed", Text = "Hoàn tất" },
            new CampaignOptionViewModel { Value = "suspended", Text = "Tạm ngưng" }
        ];

        model.TypeOptions =
        [
            new CampaignOptionViewModel { Value = "all", Text = "Tất cả" },
            new CampaignOptionViewModel { Value = "flash_sale", Text = "Flash sale" },
            new CampaignOptionViewModel { Value = "voucher_boost", Text = "Đẩy voucher" },
            new CampaignOptionViewModel { Value = "seasonal", Text = "Theo mùa" },
            new CampaignOptionViewModel { Value = "livestream", Text = "Livestream" }
        ];

        model.WalletStatusOptions =
        [
            new CampaignOptionViewModel { Value = "all", Text = "Tất cả" },
            new CampaignOptionViewModel { Value = "active", Text = "Hoạt động" },
            new CampaignOptionViewModel { Value = "paused", Text = "Tạm dừng" },
            new CampaignOptionViewModel { Value = "suspended", Text = "Tạm ngưng" }
        ];
    }

    private static void MapAdsOverview(CampaignCenterPageViewModel model, AdsOverviewApiResponse payload)
    {
        model.AdsOverview = new AdsWalletOverviewViewModel
        {
            WalletCount = payload.Stats?.WalletCount ?? 0,
            ActiveWalletCount = payload.Stats?.ActiveWalletCount ?? 0,
            RunningAdsCampaigns = payload.Stats?.RunningAdsCampaigns ?? 0,
            TotalBalance = payload.Stats?.TotalBalance ?? 0m,
            TotalReserved = payload.Stats?.TotalReserved ?? 0m,
            TotalAvailable = payload.Stats?.TotalAvailable ?? 0m,
            TotalTopup = payload.Stats?.TotalTopup ?? 0m,
            TotalSpend = payload.Stats?.TotalSpend ?? 0m
        };

        model.WalletStatusOptions = payload.Filters?.StatusOptions?.Select(MapOption).ToList() ?? new List<CampaignOptionViewModel>();
        model.AdsWallets = payload.Wallets?.Select(x => new AdsWalletRowViewModel
        {
            WalletId = x.WalletId,
            SellerId = x.SellerId,
            Balance = x.Balance,
            ReservedBalance = x.ReservedBalance,
            AvailableBalance = x.AvailableBalance,
            TotalTopup = x.TotalTopup,
            TotalSpend = x.TotalSpend,
            Status = x.Status ?? string.Empty,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList() ?? new List<AdsWalletRowViewModel>();
        model.AdsCampaigns = payload.AdsCampaigns?.Select(x => new AdsCampaignRowViewModel
        {
            AdsCampaignId = x.AdsCampaignId,
            WalletId = x.WalletId,
            SellerId = x.SellerId,
            CampaignId = x.CampaignId,
            Name = x.Name ?? string.Empty,
            Channel = x.Channel ?? string.Empty,
            Status = x.Status ?? string.Empty,
            DailyBudget = x.DailyBudget,
            TotalBudget = x.TotalBudget,
            SpendToDate = x.SpendToDate,
            StartAt = x.StartAt,
            EndAt = x.EndAt,
            CreatedAt = x.CreatedAt
        }).ToList() ?? new List<AdsCampaignRowViewModel>();
        model.RecentTopups = payload.RecentTopups?.Select(x => new AdsTopupHistoryItemViewModel
        {
            TopupId = x.TopupId,
            WalletId = x.WalletId,
            SellerId = x.SellerId,
            Amount = x.Amount,
            Status = x.Status ?? string.Empty,
            PaymentMethod = x.PaymentMethod,
            ReferenceCode = x.ReferenceCode,
            CreatedAt = x.CreatedAt
        }).ToList() ?? new List<AdsTopupHistoryItemViewModel>();
    }

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();
        var statusPrefix = $"HTTP {(int)response.StatusCode}";
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"{fallback} ({statusPrefix})";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                var message = messageElement.GetString();
                return string.IsNullOrWhiteSpace(message) ? $"{fallback} ({statusPrefix})" : $"{statusPrefix}: {message}";
            }
        }
        catch
        {
        }

        var compactBody = body.Length > 240 ? body[..240] + "..." : body;
        return $"{fallback} ({statusPrefix}): {compactBody}";
    }

    private sealed class CampaignOverviewApiModel
    {
        public int TotalCampaigns { get; set; }
        public int RunningCampaigns { get; set; }
        public int RegistrationOpenCampaigns { get; set; }
        public int ScheduledCampaigns { get; set; }
        public int TotalParticipations { get; set; }
        public int ApprovedParticipations { get; set; }
        public int PendingParticipations { get; set; }
        public int TotalSlots { get; set; }
        public int ApprovedSlots { get; set; }
        public int LiveSlots { get; set; }
        public decimal TotalBudget { get; set; }
        public List<CampaignUpcomingApiModel>? Upcoming { get; set; }
    }

    private sealed class CampaignUpcomingApiModel
    {
        public int CampaignId { get; set; }
        public string? Name { get; set; }
        public string? CampaignType { get; set; }
        public string? Status { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public DateTime RegistrationEndAt { get; set; }
    }

    private sealed class CampaignListApiResponse
    {
        public CampaignFiltersApiModel? Filters { get; set; }
        public List<CampaignListItemApiModel>? Rows { get; set; }
        public List<CampaignOptionApiModel>? StatusOptions { get; set; }
        public List<CampaignOptionApiModel>? TypeOptions { get; set; }
    }

    private sealed class CampaignFiltersApiModel
    {
        public string? Q { get; set; }
        public string? Status { get; set; }
        public string? Type { get; set; }
    }

    private sealed class CampaignOptionApiModel
    {
        public string? Value { get; set; }
        public string? Text { get; set; }
    }

    private sealed class CampaignListItemApiModel
    {
        public int CampaignId { get; set; }
        public string? Name { get; set; }
        public string? CampaignType { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public decimal? BudgetAmount { get; set; }
        public bool IsFeatured { get; set; }
        public int? VoucherCouponId { get; set; }
        public string? VoucherCode { get; set; }
        public DateTime RegistrationStartAt { get; set; }
        public DateTime RegistrationEndAt { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int ParticipationCount { get; set; }
        public int ApprovedParticipationCount { get; set; }
        public int ProductSlotCount { get; set; }
        public int ApprovedSlotCount { get; set; }
    }

    private sealed class CampaignDetailApiModel
    {
        public int CampaignId { get; set; }
        public string? Name { get; set; }
        public string? CampaignType { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public decimal? BudgetAmount { get; set; }
        public bool IsFeatured { get; set; }
        public int? VoucherCouponId { get; set; }
        public string? VoucherCode { get; set; }
        public DateTime RegistrationStartAt { get; set; }
        public DateTime RegistrationEndAt { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public List<CampaignParticipationApiModel>? Participations { get; set; }
        public List<CampaignSlotApiModel>? Slots { get; set; }
    }

    private sealed class CampaignParticipationApiModel
    {
        public int ParticipationId { get; set; }
        public int SellerId { get; set; }
        public string? Status { get; set; }
        public string? Notes { get; set; }
        public decimal? DiscountPercent { get; set; }
        public int? RequestedSlots { get; set; }
        public int? ApprovedSlots { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public int? ReviewedBy { get; set; }
    }

    private sealed class CampaignSlotApiModel
    {
        public int SlotId { get; set; }
        public int? ParticipationId { get; set; }
        public int SellerId { get; set; }
        public int ProductId { get; set; }
        public string? Status { get; set; }
        public decimal? FlashSalePrice { get; set; }
        public int? InventoryLimit { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }

    private sealed class AdsOverviewApiResponse
    {
        public AdsStatsApiModel? Stats { get; set; }
        public AdsFiltersApiModel? Filters { get; set; }
        public List<AdsWalletApiModel>? Wallets { get; set; }
        public List<AdsCampaignApiModel>? AdsCampaigns { get; set; }
        public List<AdsTopupHistoryApiModel>? RecentTopups { get; set; }
    }

    private sealed class AdsStatsApiModel
    {
        public int WalletCount { get; set; }
        public int ActiveWalletCount { get; set; }
        public int RunningAdsCampaigns { get; set; }
        public decimal TotalBalance { get; set; }
        public decimal TotalReserved { get; set; }
        public decimal TotalAvailable { get; set; }
        public decimal TotalTopup { get; set; }
        public decimal TotalSpend { get; set; }
    }

    private sealed class AdsFiltersApiModel
    {
        public string? Q { get; set; }
        public string? Status { get; set; }
        public List<CampaignOptionApiModel>? StatusOptions { get; set; }
    }

    private sealed class AdsWalletApiModel
    {
        public int WalletId { get; set; }
        public int SellerId { get; set; }
        public decimal Balance { get; set; }
        public decimal ReservedBalance { get; set; }
        public decimal AvailableBalance { get; set; }
        public decimal TotalTopup { get; set; }
        public decimal TotalSpend { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class AdsCampaignApiModel
    {
        public int AdsCampaignId { get; set; }
        public int WalletId { get; set; }
        public int SellerId { get; set; }
        public int? CampaignId { get; set; }
        public string? Name { get; set; }
        public string? Channel { get; set; }
        public string? Status { get; set; }
        public decimal DailyBudget { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal SpendToDate { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class AdsTopupHistoryApiModel
    {
        public int TopupId { get; set; }
        public int WalletId { get; set; }
        public int SellerId { get; set; }
        public decimal Amount { get; set; }
        public string? Status { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ReferenceCode { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
