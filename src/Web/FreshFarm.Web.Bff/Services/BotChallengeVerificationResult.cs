namespace FreshFarm.Web.Bff.Services;

public sealed class BotChallengeVerificationResult
{
    public bool Success { get; init; }

    public BotChallengeProvider Provider { get; init; }

    public string? ErrorMessage { get; init; }

    public string? Action { get; init; }

    public string? Hostname { get; init; }
}
