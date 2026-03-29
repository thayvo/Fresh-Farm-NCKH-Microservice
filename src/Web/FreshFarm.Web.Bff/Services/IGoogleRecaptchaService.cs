namespace FreshFarm.Web.Bff.Services;

public interface IGoogleRecaptchaService
{
    bool IsConfigured { get; }

    Task<GoogleRecaptchaVerificationResult> VerifyAsync(string token, string expectedAction, string? remoteIp, CancellationToken cancellationToken = default);
}
