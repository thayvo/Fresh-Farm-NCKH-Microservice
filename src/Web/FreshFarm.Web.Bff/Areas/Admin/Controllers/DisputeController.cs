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
public sealed class DisputeController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DisputeController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q = null,
        string? section = null,
        string? status = null,
        int? sellerId = null,
        string? selectedCaseType = null,
        int? selectedCaseId = null,
        int page = 1)
    {
        var model = new DisputeCenterPageViewModel
        {
            Query = q ?? string.Empty,
            Section = NormalizeSection(section),
            Status = NormalizeStatus(status),
            SellerId = sellerId > 0 ? sellerId : null,
            SelectedCaseType = NormalizeSection(selectedCaseType),
            SelectedCaseId = selectedCaseId > 0 ? selectedCaseId : null,
            Page = page < 1 ? 1 : page
        };

        try
        {
            var client = CreateOrderingClient();
            var response = await client.GetAsync(BuildQueueEndpoint(model));
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải trung tâm tranh chấp.");
                return View(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<DisputeQueueApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu trung tâm tranh chấp.";
                return View(model);
            }

            MapQueuePayload(model, payload);

            if (!model.SelectedCaseId.HasValue && model.Rows.Count > 0)
            {
                model.SelectedCaseType = model.Rows[0].CaseType;
                model.SelectedCaseId = model.Rows[0].CaseId;
            }

            if (model.SelectedCaseId.HasValue && !string.IsNullOrWhiteSpace(model.SelectedCaseType))
            {
                await LoadDetailsAsync(client, model);
            }
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải trung tâm tranh chấp: " + ex.Message;
        }

        return View(model);
    }

    private async Task LoadDetailsAsync(HttpClient client, DisputeCenterPageViewModel model)
    {
        var endpoint = $"/api/orders/admin/disputes/details?type={Uri.EscapeDataString(model.SelectedCaseType)}&id={model.SelectedCaseId}";
        var response = await client.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode)
        {
            ViewBag.DetailError = await ReadApiErrorAsync(response, "Không thể tải chi tiết case.");
            return;
        }

        var payload = await response.Content.ReadFromJsonAsync<DisputeDetailsApiDto>(JsonOptions);
        if (payload is null)
        {
            ViewBag.DetailError = "Không đọc được chi tiết case.";
            return;
        }

        model.Details = new DisputeCaseDetailsViewModel
        {
            CaseType = payload.CaseType ?? string.Empty,
            CaseId = payload.CaseId,
            Title = payload.Title ?? string.Empty,
            Summary = payload.Summary ?? string.Empty,
            DisplayStatus = payload.DisplayStatus ?? string.Empty,
            QueueStatus = payload.QueueStatus ?? string.Empty,
            IsSlaBreached = payload.IsSlaBreached,
            CreatedAt = payload.CreatedAt,
            UpdatedAt = payload.UpdatedAt,
            Buyer = payload.Buyer is null ? null : new DisputePersonViewModel
            {
                UserId = payload.Buyer.UserId,
                FullName = payload.Buyer.FullName ?? string.Empty,
                Phone = payload.Buyer.Phone ?? string.Empty,
                Email = payload.Buyer.Email ?? string.Empty
            },
            Seller = payload.Seller is null ? null : new DisputeSellerViewModel
            {
                SellerId = payload.Seller.SellerId,
                SellerLabel = payload.Seller.SellerLabel ?? string.Empty
            },
            Order = payload.Order is null ? null : new DisputeOrderViewModel
            {
                OrderId = payload.Order.OrderId,
                TotalAmount = payload.Order.TotalAmount,
                Status = payload.Order.Status ?? string.Empty,
                OrderDate = payload.Order.OrderDate
            },
            AfterSales = payload.AfterSales is null ? null : new DisputeAfterSalesViewModel
            {
                ReasonCode = payload.AfterSales.ReasonCode ?? string.Empty,
                Resolution = payload.AfterSales.Resolution ?? string.Empty,
                RefundAmount = payload.AfterSales.RefundAmount,
                Amount = payload.AfterSales.Amount,
                Channel = payload.AfterSales.Channel ?? string.Empty,
                Method = payload.AfterSales.Method ?? string.Empty,
                PaymentStatus = payload.AfterSales.PaymentStatus ?? string.Empty,
                ReferenceCode = payload.AfterSales.ReferenceCode ?? string.Empty
            },
            Timeline = payload.Timeline?.Select(x => new DisputeTimelineItemViewModel
            {
                Label = x.Label ?? string.Empty,
                Value = x.Value,
                Tone = x.Tone ?? string.Empty
            }).ToList() ?? new List<DisputeTimelineItemViewModel>(),
            Messages = payload.Messages?.Select(x => new DisputeMessageViewModel
            {
                Sender = x.Sender ?? string.Empty,
                Content = x.Content ?? string.Empty,
                CreatedAt = x.CreatedAt,
                IsRead = x.IsRead
            }).ToList() ?? new List<DisputeMessageViewModel>(),
            RelatedOrders = payload.RelatedOrders?.Select(x => new DisputeOrderViewModel
            {
                OrderId = x.OrderId,
                TotalAmount = x.TotalAmount,
                Status = x.Status ?? string.Empty,
                OrderDate = x.OrderDate
            }).ToList() ?? new List<DisputeOrderViewModel>(),
            RelatedCases = payload.RelatedCases?.Select(x => new DisputeRelatedCaseViewModel
            {
                CaseType = x.CaseType ?? string.Empty,
                CaseId = x.CaseId,
                Title = x.Title ?? string.Empty,
                Status = x.Status ?? string.Empty,
                Amount = x.Amount,
                CreatedAt = x.CreatedAt
            }).ToList() ?? new List<DisputeRelatedCaseViewModel>()
        };
    }

    private static void MapQueuePayload(DisputeCenterPageViewModel model, DisputeQueueApiResponse payload)
    {
        model.Page = payload.Page <= 0 ? model.Page : payload.Page;
        model.PageSize = payload.PageSize <= 0 ? model.PageSize : payload.PageSize;
        model.Total = payload.Total;
        model.TotalPages = payload.TotalPages <= 0 ? 1 : payload.TotalPages;
        model.Stats = new DisputeCenterStatsViewModel
        {
            TotalCases = payload.Stats?.TotalCases ?? 0,
            OpenCases = payload.Stats?.OpenCases ?? 0,
            PendingCases = payload.Stats?.PendingCases ?? 0,
            ResolvedCases = payload.Stats?.ResolvedCases ?? 0,
            BreachedCases = payload.Stats?.BreachedCases ?? 0,
            SupportCases = payload.Stats?.SupportCases ?? 0,
            ReturnCases = payload.Stats?.ReturnCases ?? 0,
            RefundCases = payload.Stats?.RefundCases ?? 0
        };
        model.Query = payload.Filters?.Q ?? model.Query;
        model.Section = payload.Filters?.Section ?? model.Section;
        model.Status = payload.Filters?.Status ?? model.Status;
        model.SellerId = payload.Filters?.SellerId;
        model.SectionOptions = payload.Filters?.SectionOptions?.Select(x => new DisputeOptionViewModel { Value = x.Value ?? string.Empty, Text = x.Text ?? string.Empty }).ToList() ?? new List<DisputeOptionViewModel>();
        model.StatusOptions = payload.Filters?.StatusOptions?.Select(x => new DisputeOptionViewModel { Value = x.Value ?? string.Empty, Text = x.Text ?? string.Empty }).ToList() ?? new List<DisputeOptionViewModel>();
        model.SellerOptions = payload.Filters?.SellerOptions?.Select(x => new DisputeOptionViewModel { Value = x.Value ?? string.Empty, Text = x.Text ?? string.Empty }).ToList() ?? new List<DisputeOptionViewModel>();
        model.Rows = payload.Rows?.Select(x => new DisputeQueueRowViewModel
        {
            CaseType = x.CaseType ?? string.Empty,
            CaseId = x.CaseId,
            Title = x.Title ?? string.Empty,
            Summary = x.Summary ?? string.Empty,
            DisplayStatus = x.DisplayStatus ?? string.Empty,
            QueueStatus = x.QueueStatus ?? string.Empty,
            SellerId = x.SellerId,
            SellerLabel = x.SellerLabel ?? string.Empty,
            BuyerId = x.BuyerId,
            BuyerName = x.BuyerName ?? string.Empty,
            BuyerPhone = x.BuyerPhone ?? string.Empty,
            OrderId = x.OrderId,
            Amount = x.Amount,
            UnreadCount = x.UnreadCount,
            IsSlaBreached = x.IsSlaBreached,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList() ?? new List<DisputeQueueRowViewModel>();
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

    private static string BuildQueueEndpoint(DisputeCenterPageViewModel model)
    {
        var query = new List<string>
        {
            $"page={model.Page}",
            $"pageSize={model.PageSize}",
            $"section={Uri.EscapeDataString(model.Section)}",
            $"status={Uri.EscapeDataString(model.Status)}"
        };

        if (!string.IsNullOrWhiteSpace(model.Query))
        {
            query.Add($"q={Uri.EscapeDataString(model.Query)}");
        }

        if (model.SellerId.HasValue && model.SellerId.Value > 0)
        {
            query.Add($"sellerId={model.SellerId.Value}");
        }

        return "/api/orders/admin/disputes/queue?" + string.Join("&", query);
    }

    private static string NormalizeSection(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "support" => "support",
            "returns" => "returns",
            "refunds" => "refunds",
            _ => "all"
        };
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "open" => "open",
            "pending" => "pending",
            "breached" => "breached",
            "resolved" => "resolved",
            _ => "all"
        };
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
            if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
            {
                return messageElement.GetString() ?? fallback;
            }
        }
        catch
        {
        }

        return fallback;
    }
}
