namespace FreshFarm.Ordering.Api.Models;

// Bảng ứng viên collaborative cho trang chủ.
// Từ các seed sở thích, hệ thống mở rộng sang sản phẩm thường đi cùng để tạo gợi ý home/session.
public partial class RecommendationHomeCollaborativeCandidate
{
    public int RecommendationHomeCollaborativeCandidateId { get; set; }

    // Loại phạm vi: thường là "user" hoặc "session".
    public string ScopeType { get; set; } = string.Empty;

    // Khóa của phạm vi, ví dụ user id dạng chuỗi hoặc session id.
    public string ScopeKey { get; set; } = string.Empty;

    // User thật nếu scope là user; null nếu chỉ có session ẩn danh.
    public int? UserId { get; set; }

    // Sản phẩm ứng viên được đưa vào danh sách gợi ý trang chủ.
    public int ProductId { get; set; }

    // Số tín hiệu mua cùng từ các seed dẫn tới sản phẩm ứng viên.
    public int CoPurchaseOrderCount { get; set; }

    // Số tín hiệu xem cùng từ các seed dẫn tới sản phẩm ứng viên.
    public int CoViewSessionCount { get; set; }

    // Số tín hiệu click cùng từ các seed dẫn tới sản phẩm ứng viên.
    public int CoClickSessionCount { get; set; }

    // Điểm collaborative dùng để xếp hạng ứng viên trên trang chủ.
    public double CollaborativeScore { get; set; }

    // Thời điểm batch gần nhất tính ra dòng dữ liệu này.
    public DateTime ComputedAt { get; set; }
}
