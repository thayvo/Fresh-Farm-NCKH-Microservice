using System.Net.Http.Json;
using System.Text.Json;
using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Identity.Api.Services;

public interface ICustomerNotificationPublisher
{
    bool IsConfigured { get; }

    Task PublishSellerReviewAsync(User user, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default);
}

public sealed class OrderingCustomerNotificationPublisher : ICustomerNotificationPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OrderingServiceOptions _options;
    private readonly ILogger<OrderingCustomerNotificationPublisher> _logger;

    public OrderingCustomerNotificationPublisher(
        HttpClient httpClient,
        IOptions<OrderingServiceOptions> options,
        ILogger<OrderingCustomerNotificationPublisher> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.InternalServiceKey);

    public async Task PublishSellerReviewAsync(User user, string? storeName, bool isApproved, string? reviewNote, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Ordering notification client chua duoc cau hinh day du.");
        }

        var title = isApproved
            ? "Hồ sơ seller đã được duyệt"
            : "Hồ sơ seller cần bổ sung";
        var normalizedStoreName = string.IsNullOrWhiteSpace(storeName)
            ? "gian hàng của bạn"
            : storeName.Trim();
        var message = isApproved
            ? $"Hồ sơ seller cho {normalizedStoreName} đã được duyệt. Tài khoản của bạn đã có quyền người bán và có thể tiếp tục thiết lập gian hàng."
            : $"Hồ sơ seller cho {normalizedStoreName} cần bổ sung trước khi được duyệt. Ghi chú từ đội vận hành: {(string.IsNullOrWhiteSpace(reviewNote) ? "Vui lòng đăng nhập lại và cập nhật hồ sơ." : reviewNote.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders/admin/notifications/internal")
        {
            Content = JsonContent.Create(new CreateCustomerNotificationRequest
            {
                UserId = user.UserId,
                OrderId = 0,
                NotificationType = "seller_review_update",
                Title = title,
                Message = message,
                IsPushNotification = false,
                CreatedAt = DateTime.UtcNow
            })
        };
        request.Headers.Add("X-Internal-Service-Key", _options.InternalServiceKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var messageFromApi = TryReadApiMessage(body);
        _logger.LogWarning(
            "Ordering internal notification create that bai cho user {UserId}. Status={StatusCode}, Message={Message}",
            user.UserId,
            (int)response.StatusCode,
            messageFromApi ?? body);
        throw new HttpRequestException(messageFromApi ?? "Khong tao duoc customer notification trong Ordering.");
    }

    private static string? TryReadApiMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ApiMessageResponse>(body, JsonOptions);
            return string.IsNullOrWhiteSpace(payload?.Message)
                ? null
                : payload.Message;
        }
        catch
        {
            return null;
        }
    }

    private sealed class CreateCustomerNotificationRequest
    {
        public int UserId { get; set; }

        public int OrderId { get; set; }

        public string NotificationType { get; set; } = "seller_review_update";

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public bool IsPushNotification { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    private sealed class ApiMessageResponse
    {
        public string? Message { get; set; }
    }
}
