namespace FreshFarm.Identity.Api.Dtos
{
    public sealed class LoginRequest
    {
        public string Identifier { get; set; }
        public string Password { get; set; }
    }
}
