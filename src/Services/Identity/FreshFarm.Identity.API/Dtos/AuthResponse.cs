namespace FreshFarm.Identity.Api.Dtos
{
    public sealed class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public DateTime ExpiredAtUtc { get; set; }
        public bool RequiresTwoFactor { get; set; }
        public bool RequiresTwoFactorSetup { get; set; }
        public string? TwoFactorTicket { get; set; }
        public string? ManualEntryKey { get; set; }
        public string? OtpAuthUri { get; set; }
        public string? AuthenticatorIssuer { get; set; }
        public string? AuthenticatorAccountName { get; set; }
        public string? ChallengeMessage { get; set; }
        public string AccountAccess { get; set; } = "full";
        public string ApprovalStatus { get; set; } = string.Empty;
        public bool IsPendingApproval { get; set; }
        public string? AccountStatusMessage { get; set; }
    }

    public sealed class VerifyTwoFactorLoginRequest
    {
        public string Ticket { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
