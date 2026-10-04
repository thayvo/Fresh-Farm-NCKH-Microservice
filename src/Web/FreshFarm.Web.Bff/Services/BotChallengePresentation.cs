namespace FreshFarm.Web.Bff.Services;

public sealed class BotChallengePresentation
{
    public BotChallengeProvider Provider { get; init; }

    public bool IsAvailable { get; init; }

    public string SiteKey { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }
}
