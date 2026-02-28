namespace FreshFarm.Web.Bff.Areas.Seller.Models;

public sealed class SellerFeedbackViewModel
{
    public int Id { get; set; }

    public string SenderName { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = string.Empty;

    public string? SenderPhone { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string Status { get; set; } = "new";

    public string? AdminNote { get; set; }

    public bool IsProcessed
    {
        get
        {
            var normalized = (Status ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is "processed" or "resolved" or "done";
        }
    }

    public string StatusDisplay => IsProcessed ? "Da xu ly" : "Moi";

    public string CreatedAtFormatted => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public string ShortMessage
    {
        get
        {
            var value = (Message ?? string.Empty).Trim();
            if (value.Length <= 120)
            {
                return value;
            }

            return value[..120] + "...";
        }
    }

    public string AdminNoteDisplay => string.IsNullOrWhiteSpace(AdminNote) ? "(Chua co)" : AdminNote.Trim();
}
