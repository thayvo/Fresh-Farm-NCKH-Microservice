using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Web.Bff.Options;

public sealed class GhnOrderStatusWebhookOptions : IValidatableObject
{
    public const string SectionName = "Webhooks:GhnOrderStatus";

    public bool Enabled { get; set; }

    public string Secret { get; set; } = string.Empty;

    public int DeduplicationWindowSeconds { get; set; } = 300;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && string.IsNullOrWhiteSpace(Secret))
        {
            yield return new ValidationResult(
                "Webhooks:GhnOrderStatus:Secret is required when the GHN webhook is enabled.",
                [nameof(Secret)]);
        }
    }
}
