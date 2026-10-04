namespace FreshFarm.Web.Bff.Services;

public enum BotChallengeProvider
{
    Unavailable = 0,
    CloudflareTurnstile = 1,
    GoogleRecaptcha = 2,
    LocalSvg = 3
}
