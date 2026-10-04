using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class CloudflareTurnstileService : ICloudflareTurnstileService
{
    private readonly HttpClient _httpClient;
    private readonly CloudflareTurnstileOptions _options;
    private readonly ILogger<CloudflareTurnstileService> _logger;

    public CloudflareTurnstileService(
        HttpClient httpClient,
        IOptions<CloudflareTurnstileOptions> options,
        ILogger<CloudflareTurnstileService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<BotChallengeVerificationResult> VerifyAsync(
        string token,
        string expectedAction,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return Failed("Cloudflare Turnstile chưa được cấu hình đầy đủ.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Failed("Vui lòng hoàn tất bước xác minh bạn không phải là robot.");
        }

        var formValues = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token.Trim()
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            formValues["remoteip"] = remoteIp.Trim();
        }

        try
        {
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await _httpClient.PostAsync(
                _options.VerificationEndpoint,
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Cloudflare Turnstile Siteverify returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return Failed("Không thể xác minh bảo mật lúc này. Vui lòng thử lại.");
            }

            var payload = await response.Content.ReadFromJsonAsync<TurnstileSiteVerifyResponse>(
                cancellationToken: cancellationToken);
            if (payload?.Success != true)
            {
                var errorCodes = payload?.ErrorCodes is { Length: > 0 }
                    ? string.Join(", ", payload.ErrorCodes)
                    : "unknown";
                _logger.LogWarning(
                    "Cloudflare Turnstile Siteverify rejected a token. ErrorCodes={ErrorCodes}",
                    errorCodes);
                return Failed("Xác minh bảo mật không hợp lệ hoặc đã hết hạn. Vui lòng thử lại.");
            }

            if (string.IsNullOrWhiteSpace(expectedAction) ||
                !string.Equals(payload.Action, expectedAction, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Cloudflare Turnstile action mismatch. Expected={ExpectedAction}, Actual={ActualAction}",
                    expectedAction,
                    payload.Action);
                return Failed(
                    "Xác minh bảo mật không đúng mục đích. Vui lòng thử lại.",
                    payload.Action,
                    payload.Hostname);
            }

            if (!IsAllowedHostname(payload.Hostname))
            {
                _logger.LogWarning(
                    "Cloudflare Turnstile hostname was not allowed. Hostname={Hostname}",
                    payload.Hostname);
                return Failed(
                    "Xác minh bảo mật không đúng nguồn. Vui lòng thử lại.",
                    payload.Action,
                    payload.Hostname);
            }

            return new BotChallengeVerificationResult
            {
                Success = true,
                Provider = BotChallengeProvider.CloudflareTurnstile,
                Action = payload.Action,
                Hostname = payload.Hostname
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Cloudflare Turnstile Siteverify timed out.");
            return Failed("Xác minh bảo mật đã quá thời gian chờ. Vui lòng thử lại.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Cloudflare Turnstile Siteverify could not be reached.");
            return Failed("Không thể xác minh bảo mật lúc này. Vui lòng thử lại.");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Cloudflare Turnstile Siteverify response could not be read.");
            return Failed("Không thể xác minh bảo mật lúc này. Vui lòng thử lại.");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Cloudflare Turnstile Siteverify returned invalid JSON.");
            return Failed("Không thể xác minh bảo mật lúc này. Vui lòng thử lại.");
        }
        catch (NotSupportedException exception)
        {
            _logger.LogWarning(exception, "Cloudflare Turnstile Siteverify returned an unsupported payload.");
            return Failed("Không thể xác minh bảo mật lúc này. Vui lòng thử lại.");
        }
    }

    private bool IsAllowedHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return false;
        }

        var normalizedHostname = NormalizeHostname(hostname);
        return _options.AllowedHostnames.Any(allowedHostname =>
            string.Equals(
                NormalizeHostname(allowedHostname),
                normalizedHostname,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeHostname(string hostname)
        => hostname.Trim().TrimEnd('.');

    private static BotChallengeVerificationResult Failed(
        string errorMessage,
        string? action = null,
        string? hostname = null)
        => new()
        {
            Success = false,
            Provider = BotChallengeProvider.CloudflareTurnstile,
            ErrorMessage = errorMessage,
            Action = action,
            Hostname = hostname
        };

    private sealed class TurnstileSiteVerifyResponse
    {
        public bool Success { get; set; }

        public string? Action { get; set; }

        public string? Hostname { get; set; }

        [JsonPropertyName("challenge_ts")]
        public DateTimeOffset? ChallengeTimestamp { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
