namespace FreshFarm.Web.Bff.Services;

public interface IGhnSandboxService
{
    bool IsConfigured { get; }

    int? ShopId { get; }

    Task<GhnSandboxConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<GhnSandboxShopProfileResult> GetShopProfileAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GhnSandboxLocationItem>> GetProvincesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GhnSandboxLocationItem>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GhnSandboxLocationItem>> GetWardsAsync(int districtId, CancellationToken cancellationToken = default);

    Task<GhnSandboxFeeResult> CalculateFeeAsync(GhnSandboxFeeRequest request, CancellationToken cancellationToken = default);

    Task<GhnSandboxLeadTimeResult> CalculateLeadTimeAsync(GhnSandboxLeadTimeRequest request, CancellationToken cancellationToken = default);

    Task<GhnSandboxCreateOrderResult> CreateOrderAsync(GhnSandboxCreateOrderRequest request, CancellationToken cancellationToken = default);

    Task<GhnSandboxOrderTrackingResult> GetOrderTrackingAsync(GhnSandboxOrderTrackingRequest request, CancellationToken cancellationToken = default);
}

public sealed class GhnSandboxConnectionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public int ProvinceCount { get; set; }

    public List<string> SampleProvinces { get; set; } = new();

    public GhnSandboxShopProfileResult? ShopProfile { get; set; }
}

public sealed class GhnSandboxShopProfileResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public string ShopName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public int? DistrictId { get; set; }

    public string WardCode { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public bool UsesConfiguredOrigin { get; set; }
}

public sealed class GhnSandboxLocationItem
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class GhnSandboxOriginOverride
{
    public int? FromDistrictId { get; set; }

    public string FromWardCode { get; set; } = string.Empty;

    public string ReturnPhone { get; set; } = string.Empty;

    public string ReturnAddress { get; set; } = string.Empty;

    public string PickupName { get; set; } = string.Empty;
}

public sealed class GhnSandboxFeeRequest
{
    public int ToDistrictId { get; set; }

    public string ToWardCode { get; set; } = string.Empty;

    public int? ServiceTypeId { get; set; }

    public int Height { get; set; } = 10;

    public int Length { get; set; } = 20;

    public int Width { get; set; } = 20;

    public int Weight { get; set; } = 500;

    public int InsuranceValue { get; set; } = 0;

    public string ItemName { get; set; } = "Nông sản FreshFarm";

    public int ItemQuantity { get; set; } = 1;

    public GhnSandboxOriginOverride? OriginOverride { get; set; }
}

public sealed class GhnSandboxFeeResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public int? ServiceId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public int TotalFee { get; set; }

    public int MainServiceFee { get; set; }

    public int InsuranceFee { get; set; }

    public int? FromDistrictId { get; set; }

    public string FromWardCode { get; set; } = string.Empty;
}

public sealed class GhnSandboxLeadTimeRequest
{
    public int ToDistrictId { get; set; }

    public string ToWardCode { get; set; } = string.Empty;

    public int? ServiceTypeId { get; set; }

    public GhnSandboxOriginOverride? OriginOverride { get; set; }
}

public sealed class GhnSandboxLeadTimeResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public int? ServiceId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public int? FromDistrictId { get; set; }

    public string FromWardCode { get; set; } = string.Empty;

    public long? LeadTimeUnix { get; set; }

    public DateTimeOffset? LeadTime { get; set; }

    public long? OrderDateUnix { get; set; }

    public DateTimeOffset? OrderDate { get; set; }
}

public sealed class GhnSandboxCreateOrderRequest
{
    public string ToName { get; set; } = string.Empty;

    public string ToPhone { get; set; } = string.Empty;

    public string ToAddress { get; set; } = string.Empty;

    public int ToDistrictId { get; set; }

    public string ToWardCode { get; set; } = string.Empty;

    public int? ServiceTypeId { get; set; }

    public string ClientOrderCode { get; set; } = string.Empty;

    public string Content { get; set; } = "Đơn hàng thử nghiệm FreshFarm";

    public string Note { get; set; } = string.Empty;

    public string RequiredNote { get; set; } = "KHONGCHOXEMHANG";

    public int PaymentTypeId { get; set; } = 2;

    public int CodAmount { get; set; }

    public int InsuranceValue { get; set; }

    public int Height { get; set; } = 10;

    public int Length { get; set; } = 20;

    public int Width { get; set; } = 20;

    public int Weight { get; set; } = 500;

    public List<GhnSandboxOrderItemRequest> Items { get; set; } = new();

    public GhnSandboxOriginOverride? OriginOverride { get; set; }
}

public sealed class GhnSandboxOrderItemRequest
{
    public string Name { get; set; } = "Nông sản FreshFarm";

    public string Code { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    public int Price { get; set; }

    public int Height { get; set; } = 10;

    public int Length { get; set; } = 20;

    public int Width { get; set; } = 20;

    public int Weight { get; set; } = 500;
}

public sealed class GhnSandboxCreateOrderResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public string ClientOrderCode { get; set; } = string.Empty;

    public string OrderCode { get; set; } = string.Empty;

    public string SortCode { get; set; } = string.Empty;

    public int? ServiceId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public int? TotalFee { get; set; }

    public long? ExpectedDeliveryTimeUnix { get; set; }

    public DateTimeOffset? ExpectedDeliveryTime { get; set; }

    public int? FromDistrictId { get; set; }

    public string FromWardCode { get; set; } = string.Empty;
}

public sealed class GhnSandboxOrderTrackingRequest
{
    public string OrderCode { get; set; } = string.Empty;

    public string ClientOrderCode { get; set; } = string.Empty;
}

public sealed class GhnSandboxOrderTrackingResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string ClientOrderCode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string StatusLabel { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public string FromPhone { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string ToName { get; set; } = string.Empty;

    public string ToPhone { get; set; } = string.Empty;

    public string ToAddress { get; set; } = string.Empty;

    public int? CurrentWarehouseId { get; set; }

    public string CurrentWarehouseName { get; set; } = string.Empty;

    public int? Weight { get; set; }

    public int? Length { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? CodAmount { get; set; }

    public int? TotalFee { get; set; }

    public DateTimeOffset? CreatedDate { get; set; }

    public DateTimeOffset? LeadTime { get; set; }

    public DateTimeOffset? FinishedDate { get; set; }

    public List<GhnSandboxOrderTrackingLogItem> Logs { get; set; } = new();
}

public sealed class GhnSandboxOrderTrackingLogItem
{
    public string Status { get; set; } = string.Empty;

    public string StatusLabel { get; set; } = string.Empty;

    public DateTimeOffset? UpdatedAt { get; set; }

    public int? WarehouseId { get; set; }

    public string WarehouseName { get; set; } = string.Empty;
}
