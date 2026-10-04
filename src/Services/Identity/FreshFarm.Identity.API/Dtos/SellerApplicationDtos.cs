using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Dtos;

public sealed class SellerApplicationResponseDto
{
    public bool HasApplication { get; set; }
    public bool IsSellerApproved { get; set; }
    public string Status { get; set; } = "not_applied";
    public string StatusLabel { get; set; } = "Chưa đăng ký";
    public string ReviewStatus { get; set; } = "not_applied";
    public string ReviewStatusLabel { get; set; } = "Chưa có hồ sơ";
    public string ReviewNote { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string StoreAddress { get; set; } = string.Empty;
    public string StoreEmail { get; set; } = string.Empty;
    public string StorePhone { get; set; } = string.Empty;
    public string BankAccountInfo { get; set; } = string.Empty;
    public string BankTransferInstructions { get; set; } = string.Empty;
    public string AdminNotificationEmail { get; set; } = string.Empty;
    public string PickupName { get; set; } = string.Empty;
    public string PickupPhone { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string Message { get; set; } = string.Empty;
    public SellerKycResponseDto Kyc { get; set; } = new();
    public List<SellerApplicationReviewHistoryDto> ReviewHistory { get; set; } = new();
}

public sealed class SellerApplicationReviewHistoryDto
{
    public string Action { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string ReviewStatusLabel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime ReviewedAtUtc { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string ReviewerUserName { get; set; } = string.Empty;
    public string ReviewerFullName { get; set; } = string.Empty;
}

public sealed class SellerKycResponseDto
{
    public bool HasKycProfile { get; set; }
    public bool HasIdentityDocuments { get; set; }
    public bool HasBusinessLicense { get; set; }
    public string ReviewStatus { get; set; } = "not_applied";
    public string ReviewStatusLabel { get; set; } = "Chưa có hồ sơ";
    public string ReviewNote { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public string LegalFullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string IdentityNumberMasked { get; set; } = string.Empty;
    public DateTime? IdentityIssuedDate { get; set; }
    public string IdentityIssuedPlace { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string BusinessLicenseNumber { get; set; } = string.Empty;
    public string CitizenIdFrontUrl { get; set; } = string.Empty;
    public string CitizenIdBackUrl { get; set; } = string.Empty;
    public string BusinessLicenseUrl { get; set; } = string.Empty;
    public string AdditionalDocumentUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class UpsertSellerApplicationRequestDto
{
    [Required(ErrorMessage = "Tên cửa hàng là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Tên cửa hàng tối đa 255 ký tự.")]
    public string StoreName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ hoạt động là bắt buộc.")]
    [StringLength(500, ErrorMessage = "Địa chỉ hoạt động tối đa 500 ký tự.")]
    public string StoreAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email liên hệ cửa hàng là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email liên hệ không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email liên hệ tối đa 100 ký tự.")]
    public string StoreEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại cửa hàng là bắt buộc.")]
    [RegularExpression(@"^(0\d{9}|\+84\d{9})$", ErrorMessage = "Số điện thoại phải là số di động Việt Nam hợp lệ gồm 10 số, hoặc bắt đầu bằng +84.")]
    public string StorePhone { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Thông tin tài khoản ngân hàng tối đa 500 ký tự.")]
    public string? BankAccountInfo { get; set; }

    [StringLength(1000, ErrorMessage = "Hướng dẫn chuyển khoản tối đa 1000 ký tự.")]
    public string? BankTransferInstructions { get; set; }

    [EmailAddress(ErrorMessage = "Email nhận thông báo không đúng định dạng.")]
    [StringLength(100, ErrorMessage = "Email nhận thông báo tối đa 100 ký tự.")]
    public string? AdminNotificationEmail { get; set; }

    [StringLength(150, ErrorMessage = "Tên người lấy hàng tối đa 150 ký tự.")]
    public string? PickupName { get; set; }

    [RegularExpression(@"^(0\d{9}|\+84\d{9})$", ErrorMessage = "Số điện thoại lấy hàng phải là số di động Việt Nam hợp lệ.")]
    public string? PickupPhone { get; set; }

    [StringLength(500, ErrorMessage = "Địa chỉ lấy hàng tối đa 500 ký tự.")]
    public string? PickupAddress { get; set; }

    [Required(ErrorMessage = "Họ tên trên CCCD là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Họ tên pháp lý tối đa 150 ký tự.")]
    public string LegalFullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số CCCD/CMND là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Số CCCD/CMND tối đa 50 ký tự.")]
    public string IdentityNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày cấp CCCD/CMND là bắt buộc.")]
    public DateTime? IdentityIssuedDate { get; set; }

    [Required(ErrorMessage = "Nơi cấp CCCD/CMND là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Nơi cấp CCCD/CMND tối đa 255 ký tự.")]
    public string IdentityIssuedPlace { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự.")]
    public string? TaxCode { get; set; }

    [StringLength(100, ErrorMessage = "Số giấy phép kinh doanh tối đa 100 ký tự.")]
    public string? BusinessLicenseNumber { get; set; }

    [StringLength(500, ErrorMessage = "Link CCCD mặt trước tối đa 500 ký tự.")]
    public string? CitizenIdFrontUrl { get; set; }

    [StringLength(500, ErrorMessage = "Link CCCD mặt sau tối đa 500 ký tự.")]
    public string? CitizenIdBackUrl { get; set; }

    [StringLength(500, ErrorMessage = "Link giấy phép kinh doanh tối đa 500 ký tự.")]
    public string? BusinessLicenseUrl { get; set; }

    [StringLength(500, ErrorMessage = "Link chứng từ bổ sung tối đa 500 ký tự.")]
    public string? AdditionalDocumentUrl { get; set; }

    [StringLength(1000, ErrorMessage = "Ghi chú hồ sơ tối đa 1000 ký tự.")]
    public string? Notes { get; set; }
}

public sealed class RejectSellerApplicationRequestDto
{
    [Required(ErrorMessage = "Lý do từ chối là bắt buộc.")]
    [StringLength(1000, ErrorMessage = "Lý do từ chối tối đa 1000 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
