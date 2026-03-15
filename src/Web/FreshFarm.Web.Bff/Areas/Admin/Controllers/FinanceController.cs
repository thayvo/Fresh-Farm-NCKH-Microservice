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
public sealed class FinanceController : LegacySellerControllerBase
{
    private const string AccessTokenSessionKey = "ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FinanceController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? section = null, string? q = null, string? status = null, string? followUpBucket = null, int? sellerId = null, int page = 1)
    {
        var model = new FinanceConsolePageViewModel
        {
            Section = NormalizeSection(section),
            Query = q ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant(),
            FollowUpBucket = NormalizeFollowUpBucket(followUpBucket),
            SellerId = sellerId,
            Page = page < 1 ? 1 : page,
            ShowSellerFilter = true
        };

        try
        {
            var client = CreateOrderingClient();
            var endpoint = BuildEndpoint(model.Section, model.Query, model.Status, model.FollowUpBucket, model.SellerId, model.Page, model.PageSize);
            var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải trung tâm tài chính.");
                return RenderView(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<FinanceConsoleApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu tài chính từ service.";
                return RenderView(model);
            }

            MapPayload(model, payload);
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải trung tâm tài chính: " + ex.Message;
        }

        return RenderView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TakeAction(string? section, int recordId, string? actionName, string? note, string? assigneeLabel, DateTime? followUpAt, string? q, string? status, string? followUpBucket, int? sellerId, int page = 1)
    {
        if (recordId <= 0 || string.IsNullOrWhiteSpace(actionName))
        {
            TempData["ErrorMessage"] = "Thông tin thao tác tài chính không hợp lệ.";
            return RedirectToAction(nameof(Index), new { section, q, status, followUpBucket, sellerId, page });
        }

        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/finance/actions", new
            {
                section,
                recordId,
                actionName,
                note,
                assigneeLabel,
                followUpAt
            });

            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                await ReadApiErrorAsync(response, response.IsSuccessStatusCode ? "Đã cập nhật trung tâm tài chính." : "Không thể cập nhật trung tâm tài chính.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi cập nhật trung tâm tài chính: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { section, q, status, followUpBucket, sellerId, page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TakeBulkAction(string? section, List<int>? recordIds, string? actionName, string? note, string? assigneeLabel, DateTime? followUpAt, string? q, string? status, string? followUpBucket, int? sellerId, int page = 1)
    {
        if (recordIds is null || recordIds.Count == 0 || string.IsNullOrWhiteSpace(actionName))
        {
            TempData["ErrorMessage"] = "Cần chọn bản ghi và thao tác hàng loạt hợp lệ.";
            return RedirectToAction(nameof(Index), new { section, q, status, followUpBucket, sellerId, page });
        }

        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/finance/bulk-actions", new
            {
                section,
                recordIds,
                actionName,
                note,
                assigneeLabel,
                followUpAt
            });

            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                await ReadApiErrorAsync(response, response.IsSuccessStatusCode ? "Đã xử lý lô tài chính." : "Không thể xử lý lô tài chính.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi xử lý thao tác hàng loạt: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new { section, q, status, followUpBucket, sellerId, page });
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? section = null, string? q = null, string? status = null, string? followUpBucket = null, int? sellerId = null)
    {
        try
        {
            var client = CreateOrderingClient();
            var endpoint = BuildExportEndpoint(section, q, status, followUpBucket, sellerId);
            var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] = await ReadApiErrorAsync(response, "Không thể xuất dữ liệu tài chính.");
                return RedirectToAction(nameof(Index), new { section, q, status, followUpBucket, sellerId });
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName
                ?? $"finance-{NormalizeSection(section)}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName.Trim('"'));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi xuất dữ liệu tài chính: " + ex.Message;
            return RedirectToAction(nameof(Index), new { section, q, status, followUpBucket, sellerId });
        }
    }

    private IActionResult RenderView(FinanceConsolePageViewModel model)
    {
        ViewData["AreaName"] = "Admin";
        ViewData["LayoutPath"] = "~/Areas/Admin/Views/Shared/_LayoutAdmin.cshtml";
        ViewData["FinanceScopeLabel"] = "Toàn sàn";
        return View("~/Areas/Seller/Views/Finance/Index.cshtml", model);
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

    private static string BuildEndpoint(string section, string q, string status, string followUpBucket, int? sellerId, int page, int pageSize)
    {
        var query = new List<string>
        {
            $"section={Uri.EscapeDataString(section)}",
            $"status={Uri.EscapeDataString(status)}",
            $"followUpBucket={Uri.EscapeDataString(NormalizeFollowUpBucket(followUpBucket))}",
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(q))
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
        }

        if (sellerId.HasValue && sellerId.Value > 0)
        {
            query.Add($"sellerId={sellerId.Value}");
        }

        return "/api/orders/admin/finance/console?" + string.Join("&", query);
    }

    private static void MapPayload(FinanceConsolePageViewModel model, FinanceConsoleApiResponse payload)
    {
        model.Scope = payload.Scope ?? string.Empty;
        model.Section = NormalizeSection(payload.Section);
        model.Page = payload.Page <= 0 ? model.Page : payload.Page;
        model.PageSize = payload.PageSize <= 0 ? model.PageSize : payload.PageSize;
        model.Total = payload.Total;
        model.TotalPages = payload.TotalPages <= 0 ? 1 : payload.TotalPages;
        model.Query = payload.Filters?.Q ?? model.Query;
        model.Status = payload.Filters?.Status ?? model.Status;
        model.FollowUpBucket = NormalizeFollowUpBucket(payload.Filters?.FollowUpBucket);
        model.SellerId = payload.Filters?.SellerId;
        model.Stats = new FinanceConsoleStatsViewModel
        {
            GrossMerchandiseValue = payload.Stats?.GrossMerchandiseValue ?? 0m,
            CapturedPayments = payload.Stats?.CapturedPayments ?? 0m,
            PlatformCommission = payload.Stats?.PlatformCommission ?? 0m,
            PendingPayoutAmount = payload.Stats?.PendingPayoutAmount ?? 0m,
            RefundedAmount = payload.Stats?.RefundedAmount ?? 0m,
            OpenReturns = payload.Stats?.OpenReturns ?? 0,
            OpenRefunds = payload.Stats?.OpenRefunds ?? 0,
            SellerCount = payload.Stats?.SellerCount ?? 0,
            OverdueFollowUps = payload.Stats?.OverdueFollowUps ?? 0,
            DueSoonFollowUps = payload.Stats?.DueSoonFollowUps ?? 0,
            NoFollowUpCount = payload.Stats?.NoFollowUpCount ?? 0
        };
        model.SectionCounts = new FinanceSectionCountsViewModel
        {
            Payouts = payload.SectionCounts?.Payouts ?? 0,
            Refunds = payload.SectionCounts?.Refunds ?? 0,
            Returns = payload.SectionCounts?.Returns ?? 0
        };
        model.SellerOptions = payload.Filters?.SellerOptions?
            .Select(x => new FinanceOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            })
            .ToList() ?? new List<FinanceOptionViewModel>();
        model.StatusOptions = payload.Filters?.StatusOptions?
            .Select(x => new FinanceOptionViewModel
            {
                Value = x.Value ?? string.Empty,
                Text = x.Text ?? string.Empty
            })
            .ToList() ?? new List<FinanceOptionViewModel>();
        model.Rows = payload.Rows?
            .Select(x => new FinanceConsoleRowViewModel
            {
                RecordId = x.RecordId,
                OrderId = x.OrderId,
                SellerId = x.SellerId,
                SellerLabel = x.SellerLabel ?? string.Empty,
                OrderCount = x.OrderCount,
                AmountGross = x.AmountGross,
                FeeAmount = x.FeeAmount,
                AmountNet = x.AmountNet,
                RefundAmount = x.RefundAmount,
                Status = x.Status ?? string.Empty,
                Method = x.Method ?? string.Empty,
                Provider = x.Provider ?? string.Empty,
                ReferenceCode = x.ReferenceCode ?? string.Empty,
                ReasonCode = x.ReasonCode ?? string.Empty,
                Resolution = x.Resolution ?? string.Empty,
                ItemName = x.ItemName ?? string.Empty,
                CreatedAt = x.CreatedAt,
                ScheduledAt = x.ScheduledAt,
                ProcessedAt = x.ProcessedAt,
                PaidAt = x.PaidAt,
                ReconciliationStatus = x.ReconciliationStatus ?? string.Empty,
                NextStep = x.NextStep ?? string.Empty,
                AssignedOwner = x.AssignedOwner ?? string.Empty,
                ExportedAt = x.ExportedAt,
                FollowUpAt = x.FollowUpAt,
                FollowUpNote = x.FollowUpNote ?? string.Empty,
                ReminderSentAt = x.ReminderSentAt,
                ReminderNote = x.ReminderNote ?? string.Empty,
                PriorityKey = x.PriorityKey ?? string.Empty,
                PriorityLabel = x.PriorityLabel ?? string.Empty,
                PriorityReason = x.PriorityReason ?? string.Empty,
                LastActionSummary = x.LastActionSummary ?? string.Empty
            })
            .ToList() ?? new List<FinanceConsoleRowViewModel>();
        model.OwnerSummary = payload.OwnerSummary?
            .Select(x => new FinanceOwnerSummaryViewModel
            {
                OwnerLabel = x.OwnerLabel ?? string.Empty,
                ItemCount = x.ItemCount,
                OverdueCount = x.OverdueCount,
                DueSoonCount = x.DueSoonCount,
                NoFollowUpCount = x.NoFollowUpCount
            })
            .ToList() ?? new List<FinanceOwnerSummaryViewModel>();
    }

    private static string BuildExportEndpoint(string? section, string? q, string? status, string? followUpBucket, int? sellerId)
    {
        var query = new List<string>
        {
            $"section={Uri.EscapeDataString(NormalizeSection(section))}",
            $"status={Uri.EscapeDataString(string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant())}",
            $"followUpBucket={Uri.EscapeDataString(NormalizeFollowUpBucket(followUpBucket))}"
        };

        if (!string.IsNullOrWhiteSpace(q))
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
        }

        if (sellerId.HasValue && sellerId.Value > 0)
        {
            query.Add($"sellerId={sellerId.Value}");
        }

        return "/api/orders/admin/finance/export?" + string.Join("&", query);
    }

    private static string NormalizeSection(string? section)
    {
        var normalized = (section ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "refunds" => "refunds",
            "returns" => "returns",
            _ => "payouts"
        };
    }

    private static string NormalizeFollowUpBucket(string? followUpBucket)
    {
        var normalized = (followUpBucket ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "overdue" => "overdue",
            "due-soon" or "due_soon" => "due-soon",
            "no-follow-up" or "no_follow_up" or "unscheduled" => "no-follow-up",
            "scheduled" => "scheduled",
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

