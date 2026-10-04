namespace FreshFarm.Web.Bff.Services;

public sealed class BotChallengeVerificationRequest
{
    public BotChallengePurpose Purpose { get; init; }

    public string Token { get; init; } = string.Empty;

    public string? RemoteIp { get; init; }

    public string? ExpectedLocalCode { get; init; }

    public string? SubmittedLocalCode { get; init; }
}
