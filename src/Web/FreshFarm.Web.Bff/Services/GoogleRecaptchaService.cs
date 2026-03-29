using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class GoogleRecaptchaService : IGoogleRecaptchaService
{
    private readonly HttpClient _httpClient;
    private readonly GoogleRecaptchaOptions _options;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<GoogleRecaptchaService> _logger;

    public GoogleRecaptchaService(
        HttpClient httpClient,
        IOptions<GoogleRecaptchaOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<GoogleRecaptchaService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<GoogleRecaptchaVerificationResult> VerifyAsync(
        string token,
        string expectedAction,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        if (CanUseDevelopmentBypass(token))
        {
            _logger.LogInformation(
                "Bo qua Google reCAPTCHA verify cho local development action {Action}.",
                expectedAction);

            return new GoogleRecaptchaVerificationResult
            {
                Success = true,
                Score = 1.0m,
                Action = expectedAction
            };
        }

        if (!_options.IsConfigured)
        {
            return new GoogleRecaptchaVerificationResult
            {
                Success = false,
                ErrorMessage = "Google reCAPTCHA chưa được cấu hình."
            };
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new GoogleRecaptchaVerificationResult
            {
                Success = false,
                ErrorMessage = "Vui lòng xác nhận bạn không phải là robot."
            };
        }

        var formValues = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token.Trim()
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            formValues["remoteip"] = remoteIp;
        }

        using var content = new FormUrlEncodedContent(formValues);

        using var response = await _httpClient.PostAsync(_options.VerificationEndpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Google reCAPTCHA verify tra ve status bat thuong: {StatusCode}",
                (int)response.StatusCode);

            return new GoogleRecaptchaVerificationResult
            {
                Success = false,
                ErrorMessage = "Không thể xác minh reCAPTCHA lúc này. Vui lòng thử lại."
            };
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleRecaptchaSiteVerifyResponse>(cancellationToken: cancellationToken);
        if (payload?.Success == true)
        {
            if (!string.IsNullOrWhiteSpace(expectedAction) &&
                !string.Equals(payload.Action, expectedAction, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Google reCAPTCHA action khong khop. Expected={ExpectedAction}, Actual={ActualAction}",
                    expectedAction,
                    payload.Action);

                return new GoogleRecaptchaVerificationResult
                {
                    Success = false,
                    ErrorMessage = "Xác minh reCAPTCHA không hợp lệ. Vui lòng thử lại.",
                    Score = payload.Score,
                    Action = payload.Action
                };
            }

            if (payload.Score.HasValue && payload.Score.Value < _options.MinimumScore)
            {
                _logger.LogWarning(
                    "Google reCAPTCHA score qua thap. Score={Score}, Threshold={Threshold}, Action={Action}",
                    payload.Score.Value,
                    _options.MinimumScore,
                    payload.Action);

                return new GoogleRecaptchaVerificationResult
                {
                    Success = false,
                    ErrorMessage = "Yêu cầu đăng ký bị đánh giá là không an toàn. Vui lòng thử lại.",
                    Score = payload.Score,
                    Action = payload.Action
                };
            }

            return new GoogleRecaptchaVerificationResult
            {
                Success = true,
                Score = payload.Score,
                Action = payload.Action
            };
        }

        var errorCodes = payload?.ErrorCodes is { Length: > 0 }
            ? string.Join(", ", payload.ErrorCodes)
            : "unknown";
        _logger.LogWarning("Google reCAPTCHA verify that bai. ErrorCodes={ErrorCodes}", errorCodes);

        return new GoogleRecaptchaVerificationResult
        {
            Success = false,
            ErrorMessage = "Xác minh reCAPTCHA không hợp lệ hoặc đã hết hạn. Vui lòng thử lại.",
            Score = payload?.Score,
            Action = payload?.Action
        };
    }

    private bool CanUseDevelopmentBypass(string token)
    {
        return _hostEnvironment.IsDevelopment()
            && _options.AllowDevelopmentBypass
            && !string.IsNullOrWhiteSpace(_options.DevelopmentBypassToken)
            && string.Equals(token?.Trim(), _options.DevelopmentBypassToken, StringComparison.Ordinal);
    }

    private sealed class GoogleRecaptchaSiteVerifyResponse
    {
        public bool Success { get; set; }

        public decimal? Score { get; set; }

        public string? Action { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
