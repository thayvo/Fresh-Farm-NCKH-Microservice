using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Dtos
{
    public sealed class ExternalLoginRequest
    {
        [Required]
        [StringLength(50)]
        public string Provider { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string ProviderSubject { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        public bool EmailVerified { get; set; }

        [StringLength(253)]
        public string? HostedDomain { get; set; }

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(255)]
        public string? AvatarUrl { get; set; }
    }
}
