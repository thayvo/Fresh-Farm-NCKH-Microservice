namespace FreshFarm.Web.Bff.Options;

public sealed class GhnOrderStatusWebhookOptions
{
    public const string SectionName = "Webhooks:GhnOrderStatus";

    public bool Enabled { get; set; }

    public string Secret { get; set; } = string.Empty;

    public int DeduplicationWindowSeconds { get; set; } = 300;
}
