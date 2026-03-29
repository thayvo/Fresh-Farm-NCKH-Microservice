namespace FreshFarm.Web.Bff.Services;

public sealed class GoogleRecaptchaVerificationResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public decimal? Score { get; init; }

    public string? Action { get; init; }
}
