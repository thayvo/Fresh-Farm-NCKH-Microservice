namespace FreshFarm.Identity.Api.Dtos
{
    public sealed class LoginRequest
    {
        public string Identifier { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ClientLane { get; set; }
    }
}
