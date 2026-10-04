using FreshFarm.Web.Bff.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FreshFarm.Web.Bff.Services;

public sealed class BotChallengeService : IBotChallengeService
{
    private readonly BotChallengeOptions _options;
    private readonly CloudflareTurnstileOptions _turnstileOptions;
    private readonly GoogleRecaptchaOptions _googleRecaptchaOptions;
    private readonly ICloudflareTurnstileService _turnstileService;
    private readonly IGoogleRecaptchaService _googleRecaptchaService;
    private readonly ISignUpCaptchaService _localCaptchaService;
    private readonly IHostEnvironment _hostEnvironment;

    public BotChallengeService(
        IOptions<BotChallengeOptions> options,
        IOptions<CloudflareTurnstileOptions> turnstileOptions,
        IOptions<GoogleRecaptchaOptions> googleRecaptchaOptions,
        ICloudflareTurnstileService turnstileService,
        IGoogleRecaptchaService googleRecaptchaService,
        ISignUpCaptchaService localCaptchaService,
        IHostEnvironment hostEnvironment)
    {
        _options = options.Value;
        _turnstileOptions = turnstileOptions.Value;
        _googleRecaptchaOptions = googleRecaptchaOptions.Value;
        _turnstileService = turnstileService;
        _googleRecaptchaService = googleRecaptchaService;
        _localCaptchaService = localCaptchaService;
        _hostEnvironment = hostEnvironment;
    }

    public BotChallengePresentation GetPresentation(BotChallengePurpose purpose)
    {
        var provider = ResolveConfiguredProvider();
        var action = GetExpectedAction(purpose);

        return provider switch
        {
            BotChallengeProvider.CloudflareTurnstile when _turnstileService.IsConfigured => new BotChallengePresentation
            {
                Provider = provider,
                IsAvailable = true,
                SiteKey = _turnstileOptions.SiteKey,
                Action = action
            },
            BotChallengeProvider.GoogleRecaptcha when _googleRecaptchaService.IsConfigured => new BotChallengePresentation
            {
                Provider = provider,
                IsAvailable = true,
                SiteKey = _googleRecaptchaOptions.SiteKey,
                Action = action
            },
            BotChallengeProvider.LocalSvg when CanUseLocalSvg() => new BotChallengePresentation
            {
                Provider = provider,
                IsAvailable = true,
                Action = action
            },
            _ => UnavailablePresentation(action)
        };
    }

    public async Task<BotChallengeVerificationResult> VerifyAsync(
        BotChallengeVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var presentation = GetPresentation(request.Purpose);
        if (!presentation.IsAvailable)
        {
            return new BotChallengeVerificationResult
            {
                Success = false,
                Provider = BotChallengeProvider.Unavailable,
                ErrorMessage = presentation.ErrorMessage
            };
        }

        switch (presentation.Provider)
        {
            case BotChallengeProvider.CloudflareTurnstile:
                return await _turnstileService.VerifyAsync(
                    request.Token,
                    presentation.Action,
                    request.RemoteIp,
                    cancellationToken);

            case BotChallengeProvider.GoogleRecaptcha:
            {
                var googleResult = await _googleRecaptchaService.VerifyAsync(
                    request.Token,
                    presentation.Action,
                    request.RemoteIp,
                    cancellationToken);
                return new BotChallengeVerificationResult
                {
                    Success = googleResult.Success,
                    Provider = BotChallengeProvider.GoogleRecaptcha,
                    ErrorMessage = googleResult.ErrorMessage,
                    Action = googleResult.Action
                };
            }

            case BotChallengeProvider.LocalSvg:
            {
                var success = _localCaptchaService.Matches(
                    request.ExpectedLocalCode,
                    request.SubmittedLocalCode);
                return new BotChallengeVerificationResult
                {
                    Success = success,
                    Provider = BotChallengeProvider.LocalSvg,
                    ErrorMessage = success
                        ? null
                        : "Mã xác nhận không đúng hoặc đã hết hạn. Vui lòng thử lại.",
                    Action = presentation.Action
                };
            }

            default:
                return new BotChallengeVerificationResult
                {
                    Success = false,
                    Provider = BotChallengeProvider.Unavailable,
                    ErrorMessage = "Tính năng xác minh bảo mật chưa sẵn sàng. Vui lòng thử lại sau."
                };
        }
    }

    private BotChallengeProvider ResolveConfiguredProvider()
    {
        var configuredProvider = _options.Provider?.Trim() ?? string.Empty;
        if (configuredProvider.Equals("Turnstile", StringComparison.OrdinalIgnoreCase) ||
            configuredProvider.Equals("CloudflareTurnstile", StringComparison.OrdinalIgnoreCase))
        {
            return BotChallengeProvider.CloudflareTurnstile;
        }

        if (configuredProvider.Equals("Recaptcha", StringComparison.OrdinalIgnoreCase) ||
            configuredProvider.Equals("GoogleRecaptcha", StringComparison.OrdinalIgnoreCase))
        {
            return BotChallengeProvider.GoogleRecaptcha;
        }

        if (configuredProvider.Equals("LocalSvg", StringComparison.OrdinalIgnoreCase))
        {
            return BotChallengeProvider.LocalSvg;
        }

        return BotChallengeProvider.Unavailable;
    }

    private string GetExpectedAction(BotChallengePurpose purpose)
        => purpose switch
        {
            BotChallengePurpose.SignUp => _options.SignUpAction,
            BotChallengePurpose.ForgotPassword => _options.ForgotPasswordAction,
            _ => string.Empty
        };

    private bool CanUseLocalSvg()
        => _hostEnvironment.IsDevelopment() && _options.AllowLocalSvgInDevelopment;

    private static BotChallengePresentation UnavailablePresentation(string action)
        => new()
        {
            Provider = BotChallengeProvider.Unavailable,
            IsAvailable = false,
            Action = action,
            ErrorMessage = "Tính năng xác minh bảo mật chưa được cấu hình an toàn. Vui lòng thử lại sau."
        };
}
