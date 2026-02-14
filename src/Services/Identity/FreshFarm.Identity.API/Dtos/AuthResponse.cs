namespace FreshFarm.Identity.Api.Dtos
{
    public sealed class AuthResponse
    {
        public string AccessToken { get; set; }
        public DateTime ExpiredAtUtc { get; set; }
    }
}
