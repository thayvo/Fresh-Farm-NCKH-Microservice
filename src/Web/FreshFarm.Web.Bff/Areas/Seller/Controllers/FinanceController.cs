using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Web.Bff.Areas.Admin.Models;
using FreshFarm.Web.Bff.Areas.Seller.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreshFarm.Web.Bff.Areas.Seller.Controllers;

[Authorize(Roles = "Seller")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Area("Seller")]
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
    public async Task<IActionResult> Index(string? section = null, string? q = null, string? status = null, int page = 1)
    {
        var model = new FinanceConsolePageViewModel
        {
            Section = NormalizeSection(section),
            Query = q ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant(),
            Page = page < 1 ? 1 : page,
            ShowSellerFilter = false
        };

        try
        {
            var client = CreateOrderingClient();
            var endpoint = BuildEndpoint(model.Section, model.Query, model.Status, model.Page, model.PageSize);
            var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode)
            {
                ViewBag.Error = await ReadApiErrorAsync(response, "Không thể tải đối soát thanh toán.");
                return RenderView(model);
            }

            var payload = await response.Content.ReadFromJsonAsync<FinanceConsoleApiResponse>(JsonOptions);
            if (payload is null)
            {
                ViewBag.Error = "Không đọc được dữ liệu đối soát từ service.";
                return RenderView(model);
            }

            MapPayload(model, payload);
            await LoadSellerBankInfoAsync(model);
            model.ShowSellerFilter = false;
        }
        catch (Exception ex)
        {
            ViewBag.Error = "Lỗi khi tải đối soát thanh toán: " + ex.Message;
        }

        return RenderView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(decimal amount, string? bankName, string? bankAccountName, string? bankAccountNumber, string? note)
    {
        if (amount <= 0m)
        {
            TempData["ErrorMessage"] = "Số tiền nhận phải lớn hơn 0.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/finance/seller-withdrawals", new
            {
                amount,
                bankName,
                bankAccountName,
                bankAccountNumber,
                note
            });

            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                await ReadApiErrorAsync(response, response.IsSuccessStatusCode ? "Đã ghi nhận nhận tiền." : "Không thể ghi nhận nhận tiền.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi khi ghi nhận nhận tiền: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private IActionResult RenderView(FinanceConsolePageViewModel model)
    {
        ViewData["AreaName"] = "Seller";
        ViewData["FinanceScopeLabel"] = "Cửa hàng của tôi";
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

    private HttpClient CreateIdentityClient()
    {
        var client = _httpClientFactory.CreateClient("Identity");
        client.DefaultRequestHeaders.Remove("Authorization");

        var token = GetAccessToken(AccessTokenSessionKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private async Task LoadSellerBankInfoAsync(FinanceConsolePageViewModel model)
    {
        try
        {
            var client = CreateIdentityClient();
            var response = await client.GetAsync("/auth/seller-application/me");
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var profile = await response.Content.ReadFromJsonAsync<SellerApplicationBankDto>(JsonOptions);
            ApplyBankInfo(model, profile?.BankAccountInfo);
        }
        catch
        {
        }
    }

    private static void ApplyBankInfo(FinanceConsolePageViewModel model, string? bankAccountInfo)
    {
        if (string.IsNullOrWhiteSpace(bankAccountInfo))
        {
            return;
        }

        var normalized = bankAccountInfo.Replace("\r", "\n");
        var parts = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        model.BankName = PickBankField(parts, "ngân hàng", "ngan hang", "bank") ?? model.BankName;
        model.BankAccountName = PickBankField(parts, "chủ tài khoản", "chu tai khoan", "tên tài khoản", "ten tai khoan", "account name") ?? model.BankAccountName;
        model.BankAccountNumber = PickBankField(parts, "số tài khoản", "so tai khoan", "stk", "account number") ?? model.BankAccountNumber;

        if (parts.Count == 1 && model.BankName == "Vietcombank")
        {
            model.BankName = parts[0];
        }
    }

    private static string? PickBankField(IEnumerable<string> parts, params string[] keys)
    {
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (!keys.Any(key => lower.Contains(key)))
            {
                continue;
            }

            var separatorIndex = part.IndexOf(':');
            return separatorIndex >= 0 && separatorIndex < part.Length - 1
                ? part[(separatorIndex + 1)..].Trim()
                : part.Trim();
        }

        return null;
    }

    private static string BuildEndpoint(string section, string q, string status, int page, int pageSize)
    {
        var query = new List<string>
        {
            $"section={Uri.EscapeDataString(section)}",
            $"status={Uri.EscapeDataString(status)}",
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(q))
        {
            query.Add($"q={Uri.EscapeDataString(q)}");
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
        model.Stats = new FinanceConsoleStatsViewModel
        {
            GrossMerchandiseValue = payload.Stats?.GrossMerchandiseValue ?? 0m,
            CapturedPayments = payload.Stats?.CapturedPayments ?? 0m,
            PlatformCommission = payload.Stats?.PlatformCommission ?? 0m,
            SellerEarning = payload.Stats?.SellerEarning ?? 0m,
            ReconciliationGrossMerchandiseValue = payload.Stats?.ReconciliationGrossMerchandiseValue ?? 0m,
            ReconciliationPlatformCommission = payload.Stats?.ReconciliationPlatformCommission ?? 0m,
            ReconciliationSellerEarning = payload.Stats?.ReconciliationSellerEarning ?? 0m,
            WithdrawableAmount = payload.Stats?.WithdrawableAmount ?? 0m,
            PendingSellerPayoutAmount = payload.Stats?.PendingSellerPayoutAmount ?? 0m,
            PaidPayoutAmount = payload.Stats?.PaidPayoutAmount ?? 0m,
            WithdrawnAmount = payload.Stats?.WithdrawnAmount ?? 0m,
            RemainingWithdrawableAmount = payload.Stats?.RemainingWithdrawableAmount ?? 0m,
            PendingPayoutAmount = payload.Stats?.PendingPayoutAmount ?? 0m,
            RefundedAmount = payload.Stats?.RefundedAmount ?? 0m,
            OpenReturns = payload.Stats?.OpenReturns ?? 0,
            OpenRefunds = payload.Stats?.OpenRefunds ?? 0,
            SellerCount = payload.Stats?.SellerCount ?? 0
        };
        model.SectionCounts = new FinanceSectionCountsViewModel
        {
            Payouts = payload.SectionCounts?.Payouts ?? 0,
            Refunds = payload.SectionCounts?.Refunds ?? 0,
            Returns = payload.SectionCounts?.Returns ?? 0
        };
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
                PaidAt = x.PaidAt
            })
            .ToList() ?? new List<FinanceConsoleRowViewModel>();
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

    private sealed class SellerApplicationBankDto
    {
        public string? BankAccountInfo { get; set; }
    }
}
