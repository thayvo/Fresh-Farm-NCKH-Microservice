namespace FreshFarm.Identity.Api.Dtos
{
    public sealed class RegisterRequest
    {
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Password { get; set; }
        public string RoleName { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
