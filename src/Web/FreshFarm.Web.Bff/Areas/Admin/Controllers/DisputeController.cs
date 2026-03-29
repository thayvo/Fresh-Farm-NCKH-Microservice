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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TakeAction(
        string? caseType,
        int caseId,
        string? actionName,
        string? note,
        string? assigneeLabel,
        int? slaHours,
        string? evidenceNote,
        string? q,
        string? section,
        string? status,
        int? sellerId,
        string? selectedCaseType,
        int? selectedCaseId,
        int page = 1)
    {
        if (caseId <= 0 || string.IsNullOrWhiteSpace(caseType) || string.IsNullOrWhiteSpace(actionName))
        {
            TempData["ErrorMessage"] = "Thong tin case/action khong hop le.";
            return RedirectToAction(nameof(Index), new { q, section, status, sellerId, selectedCaseType, selectedCaseId, page });
        }

        try
        {
            var client = CreateOrderingClient();
            var response = await client.PostAsJsonAsync("/api/orders/admin/disputes/actions", new
            {
                caseType,
                caseId,
                actionName,
                note,
                assigneeLabel,
                slaHours,
                evidenceNote
            });

            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] =
                await ReadApiErrorAsync(response, response.IsSuccessStatusCode ? "Da cap nhat case." : "Khong the cap nhat case.");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Loi khi cap nhat case: " + ex.Message;
        }

        return RedirectToAction(nameof(Index), new
        {
            q,
            section,
            status,
            sellerId,
            selectedCaseType = caseType ?? selectedCaseType,
            selectedCaseId = caseId > 0 ? caseId : selectedCaseId,
            page
        });
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
            Assignment = payload.Assignment is null ? null : new DisputeAssignmentViewModel
            {
                OwnerLabel = payload.Assignment.OwnerLabel ?? string.Empty,
                AssignedAt = payload.Assignment.AssignedAt
            },
            Sla = payload.Sla is null ? null : new DisputeSlaViewModel
            {
                TargetResolutionAt = payload.Sla.TargetResolutionAt,
                Source = payload.Sla.Source ?? string.Empty,
                IsBreached = payload.Sla.IsBreached
            },
            Timeline = DeduplicateTimeline(payload.Timeline).Select(x => new DisputeTimelineItemViewModel
            {
                Label = x.Label ?? string.Empty,
                Value = x.Value,
                Tone = x.Tone ?? string.Empty
            }).ToList(),
            ActivityItems = DeduplicateActivities(payload.ActivityItems).Select(x => new DisputeActivityItemViewModel
            {
                Label = x.Label ?? string.Empty,
                Summary = x.Summary ?? string.Empty,
                CreatedAt = x.CreatedAt,
                ActorUserId = x.ActorUserId
            }).ToList(),
            EvidenceItems = DeduplicateEvidence(payload.EvidenceItems).Select(x => new DisputeEvidenceItemViewModel
            {
                Note = x.Note ?? string.Empty,
                CreatedAt = x.CreatedAt,
                ActorUserId = x.ActorUserId
            }).ToList(),
            Messages = DeduplicateMessages(payload.Messages).Select(x => new DisputeMessageViewModel
            {
                Sender = x.Sender ?? string.Empty,
                Content = x.Content ?? string.Empty,
                CreatedAt = x.CreatedAt,
                IsRead = x.IsRead
            }).ToList(),
            RelatedOrders = DeduplicateRelatedOrders(payload.RelatedOrders).Select(x => new DisputeOrderViewModel
            {
                OrderId = x.OrderId,
                TotalAmount = x.TotalAmount,
                Status = x.Status ?? string.Empty,
                OrderDate = x.OrderDate
            }).ToList(),
            RelatedCases = DeduplicateRelatedCases(payload.RelatedCases).Select(x => new DisputeRelatedCaseViewModel
            {
                CaseType = x.CaseType ?? string.Empty,
                CaseId = x.CaseId,
                Title = x.Title ?? string.Empty,
                Status = x.Status ?? string.Empty,
                Amount = x.Amount,
                CreatedAt = x.CreatedAt
            }).ToList()
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
        model.SectionOptions = DeduplicateOptions(payload.Filters?.SectionOptions).Select(x => new DisputeOptionViewModel { Value = x.Value ?? string.Empty, Text = x.Text ?? string.Empty }).ToList();
        model.StatusOptions = DeduplicateOptions(payload.Filters?.StatusOptions).Select(x => new DisputeOptionViewModel { Value = x.Value ?? string.Empty, Text = x.Text ?? string.Empty }).ToList();
        model.SellerOptions = DeduplicateOptions(payload.Filters?.SellerOptions).Select(x => new DisputeOptionViewModel { Value = x.Value ?? string.Empty, Text = x.Text ?? string.Empty }).ToList();
        model.Rows = DeduplicateQueueRows(payload.Rows).Select(x => new DisputeQueueRowViewModel
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
            AssignedOwner = x.AssignedOwner ?? string.Empty,
            TargetResolutionAt = x.TargetResolutionAt,
            EvidenceCount = x.EvidenceCount,
            IsSlaBreached = x.IsSlaBreached,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }

    private static List<DisputeOptionApiDto> DeduplicateOptions(IEnumerable<DisputeOptionApiDto>? options)
    {
        return options?
            .Where(option => HasMeaningfulValue(option.Value))
            .GroupBy(option => option.Value!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(option => HasMeaningfulValue(option.Text))
                .ThenByDescending(CalculateOptionSignalLength)
                .First())
            .ToList() ?? new List<DisputeOptionApiDto>();
    }

    private static List<DisputeQueueRowApiDto> DeduplicateQueueRows(IEnumerable<DisputeQueueRowApiDto>? rows)
    {
        return rows?
            .Where(row => row.CaseId > 0)
            .GroupBy(row => row.CaseId)
            .Select(group => group
                .OrderByDescending(CalculateQueueRowScore)
                .ThenByDescending(CalculateQueueRowSignalLength)
                .ThenByDescending(row => row.UpdatedAt ?? row.CreatedAt)
                .First())
            .ToList() ?? new List<DisputeQueueRowApiDto>();
    }

    private static List<DisputeTimelineApiDto> DeduplicateTimeline(IEnumerable<DisputeTimelineApiDto>? items)
    {
        return items?
            .Where(item => HasMeaningfulValue(item.Label) || item.Value.HasValue)
            .GroupBy(item => $"{item.Label}|{item.Value:O}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => HasMeaningfulValue(item.Tone))
                .ThenByDescending(CalculateTimelineSignalLength)
                .First())
            .ToList() ?? new List<DisputeTimelineApiDto>();
    }

    private static List<DisputeActivityApiDto> DeduplicateActivities(IEnumerable<DisputeActivityApiDto>? items)
    {
        return items?
            .Where(item => HasMeaningfulValue(item.Label) || HasMeaningfulValue(item.Summary) || item.CreatedAt.HasValue)
            .GroupBy(item => $"{item.Label}|{item.CreatedAt:O}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CalculateActivityScore)
                .ThenByDescending(CalculateActivitySignalLength)
                .First())
            .ToList() ?? new List<DisputeActivityApiDto>();
    }

    private static List<DisputeEvidenceApiDto> DeduplicateEvidence(IEnumerable<DisputeEvidenceApiDto>? items)
    {
        return items?
            .Where(item => HasMeaningfulValue(item.Note) || item.CreatedAt.HasValue)
            .GroupBy(item => item.CreatedAt.HasValue ? item.CreatedAt.Value.ToString("O") : item.Note ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CalculateEvidenceScore)
                .ThenByDescending(CalculateEvidenceSignalLength)
                .First())
            .ToList() ?? new List<DisputeEvidenceApiDto>();
    }

    private static List<DisputeMessageApiDto> DeduplicateMessages(IEnumerable<DisputeMessageApiDto>? items)
    {
        return items?
            .Where(item => HasMeaningfulValue(item.Sender) || HasMeaningfulValue(item.Content) || item.CreatedAt.HasValue)
            .GroupBy(item => $"{item.Sender}|{item.CreatedAt:O}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(CalculateMessageScore)
                .ThenByDescending(CalculateMessageSignalLength)
                .First())
            .ToList() ?? new List<DisputeMessageApiDto>();
    }

    private static List<DisputeOrderApiDto> DeduplicateRelatedOrders(IEnumerable<DisputeOrderApiDto>? items)
    {
        return items?
            .Where(item => item.OrderId > 0)
            .GroupBy(item => item.OrderId)
            .Select(group => group
                .OrderByDescending(CalculateRelatedOrderScore)
                .ThenByDescending(CalculateRelatedOrderSignalLength)
                .ThenByDescending(item => item.OrderDate)
                .First())
            .ToList() ?? new List<DisputeOrderApiDto>();
    }

    private static List<DisputeRelatedCaseApiDto> DeduplicateRelatedCases(IEnumerable<DisputeRelatedCaseApiDto>? items)
    {
        return items?
            .Where(item => item.CaseId > 0)
            .GroupBy(item => item.CaseId)
            .Select(group => group
                .OrderByDescending(CalculateRelatedCaseScore)
                .ThenByDescending(CalculateRelatedCaseSignalLength)
                .ThenByDescending(item => item.CreatedAt)
                .First())
            .ToList() ?? new List<DisputeRelatedCaseApiDto>();
    }

    private static int CalculateOptionSignalLength(DisputeOptionApiDto option)
        => (option.Value?.Length ?? 0) + (option.Text?.Length ?? 0);

    private static int CalculateQueueRowScore(DisputeQueueRowApiDto row)
    {
        var score = 0;
        score += HasMeaningfulValue(row.CaseType) ? 1 : 0;
        score += HasMeaningfulValue(row.Title) ? 2 : 0;
        score += HasMeaningfulValue(row.Summary) ? 1 : 0;
        score += HasMeaningfulValue(row.DisplayStatus) ? 1 : 0;
        score += HasMeaningfulValue(row.QueueStatus) ? 1 : 0;
        score += row.SellerId.HasValue ? 1 : 0;
        score += HasMeaningfulValue(row.SellerLabel) ? 1 : 0;
        score += row.BuyerId > 0 ? 1 : 0;
        score += HasMeaningfulValue(row.BuyerName) ? 1 : 0;
        score += HasMeaningfulValue(row.BuyerPhone) ? 1 : 0;
        score += row.OrderId.HasValue ? 1 : 0;
        score += row.Amount.HasValue ? 1 : 0;
        score += row.UnreadCount > 0 ? 1 : 0;
        score += HasMeaningfulValue(row.AssignedOwner) ? 1 : 0;
        score += row.TargetResolutionAt.HasValue ? 1 : 0;
        score += row.EvidenceCount > 0 ? 1 : 0;
        score += row.IsSlaBreached ? 1 : 0;
        return score;
    }

    private static int CalculateQueueRowSignalLength(DisputeQueueRowApiDto row)
        => (row.CaseType?.Length ?? 0)
        + (row.Title?.Length ?? 0)
        + (row.Summary?.Length ?? 0)
        + (row.DisplayStatus?.Length ?? 0)
        + (row.QueueStatus?.Length ?? 0)
        + (row.SellerLabel?.Length ?? 0)
        + (row.BuyerName?.Length ?? 0)
        + (row.BuyerPhone?.Length ?? 0)
        + (row.AssignedOwner?.Length ?? 0);

    private static int CalculateTimelineSignalLength(DisputeTimelineApiDto item)
        => (item.Label?.Length ?? 0) + (item.Tone?.Length ?? 0);

    private static int CalculateActivityScore(DisputeActivityApiDto item)
    {
        var score = 0;
        score += HasMeaningfulValue(item.Label) ? 1 : 0;
        score += HasMeaningfulValue(item.Summary) ? 2 : 0;
        score += item.ActorUserId.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateActivitySignalLength(DisputeActivityApiDto item)
        => (item.Label?.Length ?? 0) + (item.Summary?.Length ?? 0);

    private static int CalculateEvidenceScore(DisputeEvidenceApiDto item)
    {
        var score = 0;
        score += HasMeaningfulValue(item.Note) ? 2 : 0;
        score += item.ActorUserId.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateEvidenceSignalLength(DisputeEvidenceApiDto item)
        => (item.Note?.Length ?? 0);

    private static int CalculateMessageScore(DisputeMessageApiDto item)
    {
        var score = 0;
        score += HasMeaningfulValue(item.Sender) ? 1 : 0;
        score += HasMeaningfulValue(item.Content) ? 2 : 0;
        score += item.IsRead ? 1 : 0;
        return score;
    }

    private static int CalculateMessageSignalLength(DisputeMessageApiDto item)
        => (item.Sender?.Length ?? 0) + (item.Content?.Length ?? 0);

    private static int CalculateRelatedOrderScore(DisputeOrderApiDto item)
    {
        var score = 0;
        score += item.TotalAmount != 0m ? 1 : 0;
        score += HasMeaningfulValue(item.Status) ? 1 : 0;
        score += item.OrderDate.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateRelatedOrderSignalLength(DisputeOrderApiDto item)
        => (item.Status?.Length ?? 0);

    private static int CalculateRelatedCaseScore(DisputeRelatedCaseApiDto item)
    {
        var score = 0;
        score += HasMeaningfulValue(item.CaseType) ? 1 : 0;
        score += HasMeaningfulValue(item.Title) ? 2 : 0;
        score += HasMeaningfulValue(item.Status) ? 1 : 0;
        score += item.Amount.HasValue ? 1 : 0;
        score += item.CreatedAt.HasValue ? 1 : 0;
        return score;
    }

    private static int CalculateRelatedCaseSignalLength(DisputeRelatedCaseApiDto item)
        => (item.CaseType?.Length ?? 0) + (item.Title?.Length ?? 0) + (item.Status?.Length ?? 0);

    private static bool HasMeaningfulValue(string? value) => !string.IsNullOrWhiteSpace(value);

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
