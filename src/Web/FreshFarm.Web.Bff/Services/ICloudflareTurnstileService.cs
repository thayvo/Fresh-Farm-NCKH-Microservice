namespace FreshFarm.Web.Bff.Services;

public interface ICloudflareTurnstileService
{
    bool IsConfigured { get; }

    Task<BotChallengeVerificationResult> VerifyAsync(
        string token,
        string expectedAction,
        string? remoteIp,
        CancellationToken cancellationToken = default);
}
