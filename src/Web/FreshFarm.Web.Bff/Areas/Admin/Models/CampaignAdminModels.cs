namespace FreshFarm.Web.Bff.Areas.Admin.Models;

public sealed class CampaignCenterPageViewModel
{
    public string Query { get; set; } = string.Empty;

    public string Status { get; set; } = "all";

    public string Type { get; set; } = "all";

    public int? SelectedCampaignId { get; set; }

    public CampaignOverviewViewModel Overview { get; set; } = new();

    public AdsWalletOverviewViewModel AdsOverview { get; set; } = new();

    public List<CampaignOptionViewModel> StatusOptions { get; set; } = new();

    public List<CampaignOptionViewModel> TypeOptions { get; set; } = new();

    public List<CampaignListItemViewModel> Campaigns { get; set; } = new();

    public List<CampaignOptionViewModel> WalletStatusOptions { get; set; } = new();

    public List<AdsWalletRowViewModel> AdsWallets { get; set; } = new();

    public List<AdsCampaignRowViewModel> AdsCampaigns { get; set; } = new();

    public List<AdsTopupHistoryItemViewModel> RecentTopups { get; set; } = new();

    public CampaignDetailViewModel? SelectedCampaign { get; set; }

    public CampaignUpsertInputModel Editor { get; set; } = new();

    public AdsTopupInputModel AdsTopupEditor { get; set; } = new();

    public AdsSpendInputModel AdsSpendEditor { get; set; } = new();

    public AdsCampaignInputModel AdsCampaignEditor { get; set; } = new();
}

public sealed class CampaignOverviewViewModel
{
    public int TotalCampaigns { get; set; }
    public int RunningCampaigns { get; set; }
    public int RegistrationOpenCampaigns { get; set; }
    public int ScheduledCampaigns { get; set; }
    public int TotalParticipations { get; set; }
    public int ApprovedParticipations { get; set; }
    public int PendingParticipations { get; set; }
    public int TotalSlots { get; set; }
    public int ApprovedSlots { get; set; }
    public int LiveSlots { get; set; }
    public decimal TotalBudget { get; set; }
    public List<CampaignUpcomingViewModel> Upcoming { get; set; } = new();
}

public sealed class CampaignUpcomingViewModel
{
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CampaignType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
}

public sealed class AdsWalletOverviewViewModel
{
    public int WalletCount { get; set; }
    public int ActiveWalletCount { get; set; }
    public int RunningAdsCampaigns { get; set; }
    public decimal TotalBalance { get; set; }
    public decimal TotalReserved { get; set; }
    public decimal TotalAvailable { get; set; }
    public decimal TotalTopup { get; set; }
    public decimal TotalSpend { get; set; }
}

public sealed class CampaignOptionViewModel
{
    public string Value { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class CampaignListItemViewModel
{
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CampaignType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? BudgetAmount { get; set; }
    public bool IsFeatured { get; set; }
    public int? VoucherCouponId { get; set; }
    public string? VoucherCode { get; set; }
    public DateTime RegistrationStartAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int ParticipationCount { get; set; }
    public int ApprovedParticipationCount { get; set; }
    public int ProductSlotCount { get; set; }
    public int ApprovedSlotCount { get; set; }
}

public sealed class CampaignDetailViewModel
{
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CampaignType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? BudgetAmount { get; set; }
    public bool IsFeatured { get; set; }
    public int? VoucherCouponId { get; set; }
    public string? VoucherCode { get; set; }
    public DateTime RegistrationStartAt { get; set; }
    public DateTime RegistrationEndAt { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<CampaignParticipationViewModel> Participations { get; set; } = new();
    public List<CampaignSlotViewModel> Slots { get; set; } = new();
}

public sealed class CampaignParticipationViewModel
{
    public int ParticipationId { get; set; }
    public int SellerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? RequestedSlots { get; set; }
    public int? ApprovedSlots { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedBy { get; set; }
}

public sealed class CampaignSlotViewModel
{
    public int SlotId { get; set; }
    public int? ParticipationId { get; set; }
    public int SellerId { get; set; }
    public int ProductId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? FlashSalePrice { get; set; }
    public int? InventoryLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public sealed class CampaignUpsertInputModel
{
    public int? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CampaignType { get; set; } = "flash_sale";
    public string Status { get; set; } = "draft";
    public string? Description { get; set; }
    public DateTime RegistrationStartAt { get; set; } = DateTime.Today.AddDays(1);
    public DateTime RegistrationEndAt { get; set; } = DateTime.Today.AddDays(3);
    public DateTime StartAt { get; set; } = DateTime.Today.AddDays(5);
    public DateTime EndAt { get; set; } = DateTime.Today.AddDays(7);
    public decimal? BudgetAmount { get; set; }
    public bool IsFeatured { get; set; }
    public int? VoucherCouponId { get; set; }
}

public sealed class AdsWalletRowViewModel
{
    public int WalletId { get; set; }
    public int SellerId { get; set; }
    public decimal Balance { get; set; }
    public decimal ReservedBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal TotalTopup { get; set; }
    public decimal TotalSpend { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AdsCampaignRowViewModel
{
    public int AdsCampaignId { get; set; }
    public int WalletId { get; set; }
    public int SellerId { get; set; }
    public int? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal DailyBudget { get; set; }
    public decimal TotalBudget { get; set; }
    public decimal SpendToDate { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdsTopupHistoryItemViewModel
{
    public int TopupId { get; set; }
    public int WalletId { get; set; }
    public int SellerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? ReferenceCode { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdsTopupInputModel
{
    public int SellerId { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ReferenceCode { get; set; }
    public string? Notes { get; set; }
}

public sealed class AdsSpendInputModel
{
    public int SellerId { get; set; }
    public int? AdsCampaignId { get; set; }
    public decimal Amount { get; set; }
    public string? SpendType { get; set; }
    public string? Notes { get; set; }
}

public sealed class AdsCampaignInputModel
{
    public int SellerId { get; set; }
    public int? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Channel { get; set; } = "onsite";
    public string Status { get; set; } = "draft";
    public decimal DailyBudget { get; set; }
    public decimal TotalBudget { get; set; }
    public bool ReserveBudget { get; set; } = true;
    public DateTime StartAt { get; set; } = DateTime.Today.AddDays(1);
    public DateTime EndAt { get; set; } = DateTime.Today.AddDays(7);
}
