namespace FreshFarm.Web.Bff.Services;

public interface IBotChallengeService
{
    BotChallengePresentation GetPresentation(BotChallengePurpose purpose);

    Task<BotChallengeVerificationResult> VerifyAsync(
        BotChallengeVerificationRequest request,
        CancellationToken cancellationToken = default);
}
