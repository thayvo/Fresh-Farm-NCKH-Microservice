using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreshFarm.Web.Bff.Options;
using FreshFarm.Web.Bff.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Controllers;

[ApiController]
[Route("webhooks/ghn")]
[AllowAnonymous]
public sealed class GhnWebhookController : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> StatusLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ready_to_pick"] = "Sẵn sàng lấy hàng",
            ["picking"] = "Đang lấy hàng",
            ["money_collect_picking"] = "Đang lấy hàng và thu tiền",
            ["picked"] = "Đã lấy hàng",
            ["storing"] = "Đang lưu kho",
            ["transporting"] = "Đang trung chuyển",
            ["sorting"] = "Đang phân loại",
            ["delivering"] = "Đang giao hàng",
            ["money_collect_delivering"] = "Đang giao hàng và thu tiền",
            ["delivered"] = "Đã giao hàng",
            ["delivery_fail"] = "Giao hàng thất bại",
            ["waiting_to_return"] = "Chờ hoàn hàng",
            ["return"] = "Đang hoàn hàng",
            ["return_sorting"] = "Đang phân loại hoàn hàng",
            ["returning"] = "Đang chuyển hoàn",
            ["returned"] = "Đã hoàn hàng",
            ["cancel"] = "Đã hủy",
            ["exception"] = "Đơn hàng ngoại lệ",
            ["damage"] = "Hàng hóa hư hỏng",
            ["lost"] = "Hàng hóa thất lạc"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGhnSandboxService _ghnSandboxService;
    private readonly IOptionsMonitor<OrderingServiceOptions> _orderingOptions;
    private readonly IOptionsMonitor<GhnOrderStatusWebhookOptions> _webhookOptions;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<GhnWebhookController> _logger;

    public GhnWebhookController(
        IHttpClientFactory httpClientFactory,
        IGhnSandboxService ghnSandboxService,
        IOptionsMonitor<OrderingServiceOptions> orderingOptions,
        IOptionsMonitor<GhnOrderStatusWebhookOptions> webhookOptions,
        IMemoryCache memoryCache,
        ILogger<GhnWebhookController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ghnSandboxService = ghnSandboxService;
        _orderingOptions = orderingOptions;
        _webhookOptions = webhookOptions;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    [HttpPost("order-status")]
    public async Task<IActionResult> ReceiveOrderStatus(
        [FromBody] GhnOrderStatusWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var options = _webhookOptions.CurrentValue;
        if (!options.Enabled)
        {
            return NotFound();
        }

        if (!IsAuthorizedRequest(options))
        {
            _logger.LogWarning("Ghn webhook bi tu choi do secret khong hop le.");
            return Unauthorized(new { success = false, message = "Webhook secret khong hop le." });
        }

        if (string.IsNullOrWhiteSpace(request.OrderCode) && string.IsNullOrWhiteSpace(request.ClientOrderCode))
        {
            return BadRequest(new { success = false, message = "Thieu order_code hoac client_order_code." });
        }

        var eventKey = BuildEventKey(request);
        if (IsDuplicateEvent(eventKey, options))
        {
            _logger.LogInformation(
                "Bo qua webhook GHN trung lap. EventKey={EventKey}, OrderCode={OrderCode}, ClientOrderCode={ClientOrderCode}, Type={Type}, Status={Status}",
                eventKey,
                request.OrderCode,
                request.ClientOrderCode,
                request.Type,
                request.Status);
            return Ok(new
            {
                success = true,
                message = "Webhook GHN trung lap da duoc bo qua.",
                duplicate = true,
                orderCode = request.OrderCode?.Trim(),
                clientOrderCode = request.ClientOrderCode?.Trim()
            });
        }

        _logger.LogInformation(
            "Nhan webhook GHN. EventKey={EventKey}, OrderCode={OrderCode}, ClientOrderCode={ClientOrderCode}, Type={Type}, Status={Status}",
            eventKey,
            request.OrderCode,
            request.ClientOrderCode,
            request.Type,
            request.Status);

        var orderingOptions = _orderingOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(orderingOptions.InternalServiceKey))
        {
            _logger.LogWarning("Ghn webhook khong the persist vi thieu Services:Ordering:InternalServiceKey.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Thieu internal service key." });
        }

        var metadataRequest = await BuildMetadataRequestAsync(request, cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/orders/admin/shippings/internal/ghn-metadata/by-code")
        {
            Content = JsonContent.Create(metadataRequest)
        };
        message.Headers.Add("X-Internal-Service-Key", orderingOptions.InternalServiceKey);

        var client = _httpClientFactory.CreateClient("Ordering");
        using var response = await client.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Ghn webhook khong persist duoc metadata. HTTP {StatusCode}. OrderCode={OrderCode}, ClientOrderCode={ClientOrderCode}, Response={Response}",
                (int)response.StatusCode,
                request.OrderCode,
                request.ClientOrderCode,
                responseText);
            return StatusCode((int)response.StatusCode, new
            {
                success = false,
                message = "Khong persist duoc metadata GHN.",
                response = responseText
            });
        }

        return Ok(new
        {
            success = true,
            message = "Da nhan webhook GHN.",
            duplicate = false,
            orderCode = request.OrderCode?.Trim(),
            clientOrderCode = request.ClientOrderCode?.Trim()
        });
    }

    private async Task<GhnMetadataByCodeUpsertRequest> BuildMetadataRequestAsync(
        GhnOrderStatusWebhookRequest request,
        CancellationToken cancellationToken)
    {
        if (_ghnSandboxService.IsConfigured)
        {
            var tracking = await _ghnSandboxService.GetOrderTrackingAsync(new GhnSandboxOrderTrackingRequest
            {
                OrderCode = request.OrderCode?.Trim() ?? string.Empty,
                ClientOrderCode = request.ClientOrderCode?.Trim() ?? string.Empty
            }, cancellationToken);

            if (tracking.Success)
            {
                return new GhnMetadataByCodeUpsertRequest
                {
                    OrderCode = tracking.OrderCode,
                    ClientOrderCode = tracking.ClientOrderCode,
                    Status = tracking.Status,
                    StatusLabel = tracking.StatusLabel,
                    TotalFee = tracking.TotalFee,
                    CreatedAt = tracking.CreatedDate?.UtcDateTime,
                    ExpectedDeliveryTime = tracking.LeadTime?.UtcDateTime,
                    LastSyncedAt = DateTime.UtcNow
                };
            }

            _logger.LogWarning(
                "Ghn webhook khong enrich duoc tracking, se fallback sang payload callback. OrderCode={OrderCode}, ClientOrderCode={ClientOrderCode}, Message={Message}",
                request.OrderCode,
                request.ClientOrderCode,
                tracking.Message);
        }

        return new GhnMetadataByCodeUpsertRequest
        {
            OrderCode = NormalizeText(request.OrderCode),
            ClientOrderCode = NormalizeText(request.ClientOrderCode),
            Status = NormalizeText(request.Status),
            StatusLabel = TranslateStatus(request.Status),
            TotalFee = request.TotalFee,
            CreatedAt = null,
            ExpectedDeliveryTime = null,
            LastSyncedAt = DateTime.UtcNow
        };
    }

    private bool IsAuthorizedRequest(GhnOrderStatusWebhookOptions options)
    {
        var configuredSecret = options.Secret?.Trim();
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return true;
        }

        var incomingSecret =
            Request.Query["secret"].ToString().Trim();

        if (string.IsNullOrWhiteSpace(incomingSecret))
        {
            incomingSecret = Request.Headers["X-Webhook-Secret"].ToString().Trim();
        }

        if (string.IsNullOrWhiteSpace(incomingSecret))
        {
            return false;
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var incomingBytes = Encoding.UTF8.GetBytes(incomingSecret);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, incomingBytes);
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TranslateStatus(string? status)
    {
        var normalized = NormalizeText(status);
        if (normalized is null)
        {
            return null;
        }

        return StatusLabels.TryGetValue(normalized, out var label)
            ? label
            : normalized;
    }

    private bool IsDuplicateEvent(string eventKey, GhnOrderStatusWebhookOptions options)
    {
        if (_memoryCache.TryGetValue(eventKey, out _))
        {
            return true;
        }

        _memoryCache.Set(
            eventKey,
            true,
            TimeSpan.FromSeconds(Math.Max(30, options.DeduplicationWindowSeconds)));
        return false;
    }

    private static string BuildEventKey(GhnOrderStatusWebhookRequest request)
    {
        var orderCode = NormalizeText(request.OrderCode) ?? string.Empty;
        var clientOrderCode = NormalizeText(request.ClientOrderCode) ?? string.Empty;
        var status = NormalizeText(request.Status) ?? string.Empty;
        var type = NormalizeText(request.Type) ?? string.Empty;
        var eventTime = request.Time.ValueKind == JsonValueKind.String
            ? request.Time.GetString()?.Trim() ?? string.Empty
            : request.Time.GetRawText();

        return string.Join("|", orderCode, clientOrderCode, status, type, eventTime);
    }

    public sealed class GhnOrderStatusWebhookRequest
    {
        [JsonPropertyName("OrderCode")]
        public string? OrderCode { get; set; }

        [JsonPropertyName("ClientOrderCode")]
        public string? ClientOrderCode { get; set; }

        [JsonPropertyName("Status")]
        public string? Status { get; set; }

        [JsonPropertyName("TotalFee")]
        public decimal? TotalFee { get; set; }

        [JsonPropertyName("Type")]
        public string? Type { get; set; }

        [JsonPropertyName("Time")]
        public JsonElement Time { get; set; }
    }

    public sealed class GhnMetadataByCodeUpsertRequest
    {
        public string? OrderCode { get; set; }

        public string? ClientOrderCode { get; set; }

        public string? Status { get; set; }

        public string? StatusLabel { get; set; }

        public decimal? TotalFee { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? ExpectedDeliveryTime { get; set; }

        public DateTime? LastSyncedAt { get; set; }
    }
}
