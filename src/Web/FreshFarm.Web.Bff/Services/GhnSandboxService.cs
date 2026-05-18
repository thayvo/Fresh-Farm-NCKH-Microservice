using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace FreshFarm.Web.Bff.Services;

public sealed class GhnSandboxOptions
{
    public const string SectionName = "ShippingProviders:GhnSandbox";

    public string BaseUrl { get; set; } = "https://dev-online-gateway.ghn.vn";

    public string Token { get; set; } = string.Empty;

    public int? ShopId { get; set; }

    public int? FromDistrictId { get; set; }

    public string FromWardCode { get; set; } = string.Empty;

    public string ReturnPhone { get; set; } = string.Empty;

    public string ReturnAddress { get; set; } = string.Empty;
}

public sealed class GhnSandboxService : IGhnSandboxService
{
    private static readonly IReadOnlyDictionary<string, string> TrackingStatusLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ready_to_pick"] = "Sẵn sàng lấy hàng",
            ["picking"] = "Đang lấy hàng",
            ["money_collect_picking"] = "Đang lấy hàng và thu tiền",
            ["picked"] = "Đã lấy hàng",
            ["storing"] = "Đang lưu kho",
            ["transporting"] = "Đang trung chuyển",
            ["sorting"] = "Đang phân loại",
            ["delivering"] = "Đang giao hàng",
            ["money_collect_delivering"] = "Đang giao hàng và thu tiền",
            ["delivered"] = "Đã giao hàng",
            ["delivery_fail"] = "Giao hàng thất bại",
            ["waiting_to_return"] = "Chờ hoàn hàng",
            ["return"] = "Đang hoàn hàng",
            ["return_sorting"] = "Đang phân loại hoàn hàng",
            ["returning"] = "Đang chuyển hoàn",
            ["returned"] = "Đã hoàn hàng",
            ["cancel"] = "Đã hủy",
            ["exception"] = "Đơn hàng ngoại lệ",
            ["damage"] = "Hàng hóa hư hỏng",
            ["lost"] = "Hàng hóa thất lạc"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GhnSandboxOptions _options;
    private readonly ILogger<GhnSandboxService> _logger;

    public GhnSandboxService(
        IHttpClientFactory httpClientFactory,
        IOptions<GhnSandboxOptions> options,
        ILogger<GhnSandboxService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BaseUrl) && !string.IsNullOrWhiteSpace(_options.Token);

    public int? ShopId => _options.ShopId;

    public async Task<GhnSandboxConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GhnSandboxConnectionResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa cấu hình đầy đủ GHN sandbox. Cần `BaseUrl` và `Token`."
            };
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/shiip/public-api/master-data/province");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Token", _options.Token);
        if (_options.ShopId.HasValue && _options.ShopId.Value > 0)
        {
            request.Headers.Add("ShopId", _options.ShopId.Value.ToString());
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GhnProvinceEnvelope>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = payload?.Message;
            return new GhnSandboxConnectionResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"GHN sandbox trả về HTTP {(int)response.StatusCode}."
                    : $"GHN sandbox trả lỗi: {message}"
            };
        }

        var provinces = payload?.Data ?? new List<GhnProvinceItem>();
        _logger.LogInformation("Kết nối GHN sandbox thành công. Nhận {ProvinceCount} tỉnh/thành.", provinces.Count);

        var shopProfile = await GetShopProfileAsync(cancellationToken);

        return new GhnSandboxConnectionResult
        {
            Success = true,
            ShopId = _options.ShopId,
            ProvinceCount = provinces.Count,
            SampleProvinces = provinces
                .Where(x => !string.IsNullOrWhiteSpace(x.ProvinceName))
                .Take(5)
                .Select(x => x.ProvinceName!.Trim())
                .ToList(),
            ShopProfile = shopProfile.Success ? shopProfile : null,
            Message = provinces.Count > 0
                ? "Kết nối GHN sandbox thành công. Đã đọc được danh sách tỉnh/thành."
                : "Kết nối GHN sandbox thành công nhưng chưa đọc được dữ liệu tỉnh/thành."
        };
    }

    public async Task<GhnSandboxShopProfileResult> GetShopProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GhnSandboxShopProfileResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa cấu hình đầy đủ GHN sandbox."
            };
        }

        if (!_options.ShopId.HasValue || _options.ShopId.Value <= 0)
        {
            return new GhnSandboxShopProfileResult
            {
                Success = false,
                Message = "Chưa có `ShopId` GHN hợp lệ."
            };
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        using var request = CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shop/all", new
        {
            offset = 0,
            limit = 100
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GhnShopEnvelope>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = payload?.Message;
            return new GhnSandboxShopProfileResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"GHN shop/all trả về HTTP {(int)response.StatusCode}."
                    : $"GHN shop/all trả lỗi: {message}"
            };
        }

        var shop = payload?.Data?.Shops?.FirstOrDefault(x => x.ShopId == _options.ShopId.Value);
        if (shop is null)
        {
            return new GhnSandboxShopProfileResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = $"Không tìm thấy shop `{_options.ShopId}` trong danh sách GHN sandbox."
            };
        }

        var configuredDistrictId = _options.FromDistrictId.GetValueOrDefault();
        var configuredWardCode = _options.FromWardCode?.Trim() ?? string.Empty;
        var usesConfiguredOrigin = configuredDistrictId > 0 && !string.IsNullOrWhiteSpace(configuredWardCode);

        return new GhnSandboxShopProfileResult
        {
            Success = true,
            ShopId = shop.ShopId,
            ShopName = shop.Name?.Trim() ?? string.Empty,
            Phone = shop.Phone?.Trim() ?? string.Empty,
            DistrictId = usesConfiguredOrigin ? configuredDistrictId : shop.DistrictId,
            WardCode = usesConfiguredOrigin ? configuredWardCode : (shop.WardCode?.Trim() ?? string.Empty),
            Address = BuildAddress(shop.Address, shop.WardName, shop.DistrictName, shop.ProvinceName),
            UsesConfiguredOrigin = usesConfiguredOrigin,
            Message = usesConfiguredOrigin
                ? "Đã đọc shop GHN sandbox và đang ưu tiên địa chỉ gửi cấu hình cục bộ."
                : "Đã đọc shop GHN sandbox và dùng địa chỉ shop làm địa chỉ gửi mặc định."
        };
    }

    public async Task<IReadOnlyList<GhnSandboxLocationItem>> GetProvincesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Array.Empty<GhnSandboxLocationItem>();
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        using var request = CreateRequest(HttpMethod.Get, "/shiip/public-api/master-data/province");
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GhnProvinceEnvelope>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
        {
            _logger.LogWarning("Không lấy được danh sách tỉnh/thành GHN. HTTP {StatusCode}.", (int)response.StatusCode);
            return Array.Empty<GhnSandboxLocationItem>();
        }

        return payload.Data
            .Where(x => !string.IsNullOrWhiteSpace(x.ProvinceName))
            .Select(x => new GhnSandboxLocationItem
            {
                Id = x.ProvinceID,
                Name = x.ProvinceName!.Trim()
            })
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<GhnSandboxLocationItem>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || provinceId <= 0)
        {
            return Array.Empty<GhnSandboxLocationItem>();
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        using var request = CreateRequest(HttpMethod.Post, "/shiip/public-api/master-data/district", new
        {
            province_id = provinceId
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GhnDistrictEnvelope>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
        {
            _logger.LogWarning("Không lấy được danh sách quận/huyện GHN cho tỉnh {ProvinceId}. HTTP {StatusCode}.", provinceId, (int)response.StatusCode);
            return Array.Empty<GhnSandboxLocationItem>();
        }

        return payload.Data
            .Where(x => !string.IsNullOrWhiteSpace(x.DistrictName))
            .Select(x => new GhnSandboxLocationItem
            {
                Id = x.DistrictId,
                Name = x.DistrictName!.Trim()
            })
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<GhnSandboxLocationItem>> GetWardsAsync(int districtId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || districtId <= 0)
        {
            return Array.Empty<GhnSandboxLocationItem>();
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        using var request = CreateRequest(HttpMethod.Post, "/shiip/public-api/master-data/ward", new
        {
            district_id = districtId
        });

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GhnWardEnvelope>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || payload?.Data is null)
        {
            _logger.LogWarning("Không lấy được danh sách phường/xã GHN cho quận {DistrictId}. HTTP {StatusCode}.", districtId, (int)response.StatusCode);
            return Array.Empty<GhnSandboxLocationItem>();
        }

        return payload.Data
            .Where(x => !string.IsNullOrWhiteSpace(x.WardName))
            .Select(x => new GhnSandboxLocationItem
            {
                Id = int.TryParse(x.WardCode, out var wardId) ? wardId : 0,
                Code = x.WardCode?.Trim() ?? string.Empty,
                Name = x.WardName!.Trim()
            })
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<GhnSandboxFeeResult> CalculateFeeAsync(GhnSandboxFeeRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GhnSandboxFeeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa cấu hình đầy đủ GHN sandbox. Cần `BaseUrl`, `Token` và `ShopId`."
            };
        }

        if (!_options.ShopId.HasValue || _options.ShopId.Value <= 0)
        {
            return new GhnSandboxFeeResult
            {
                Success = false,
                Message = "Chưa có `ShopId` GHN hợp lệ."
            };
        }

        if (request.ToDistrictId <= 0 || string.IsNullOrWhiteSpace(request.ToWardCode))
        {
            return new GhnSandboxFeeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Thiếu quận/huyện hoặc phường/xã nhận hàng để tính phí."
            };
        }

        var origin = await ResolveOriginAsync(request.OriginOverride, cancellationToken);
        if (origin is null)
        {
            return new GhnSandboxFeeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa xác định được địa chỉ gửi GHN. Hãy kiểm tra lại shop sandbox hoặc cấu hình `FromDistrictId`, `FromWardCode`."
            };
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        var service = await ResolveServiceAsync(client, request, origin.Value.FromDistrictId, cancellationToken);
        if (service is null)
        {
            return new GhnSandboxFeeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Không tìm được dịch vụ GHN phù hợp để tính phí."
            };
        }

        using var feeRequest = new HttpRequestMessage(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/fee");
        feeRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        feeRequest.Headers.Add("Token", _options.Token);
        feeRequest.Headers.Add("ShopId", _options.ShopId.Value.ToString());
        feeRequest.Content = JsonContent.Create(new
        {
            from_district_id = origin.Value.FromDistrictId,
            from_ward_code = origin.Value.FromWardCode,
            service_id = service.ServiceId,
            to_district_id = request.ToDistrictId,
            to_ward_code = request.ToWardCode.Trim(),
            height = Math.Max(1, request.Height),
            length = Math.Max(1, request.Length),
            width = Math.Max(1, request.Width),
            weight = Math.Max(1, request.Weight),
            insurance_value = Math.Max(0, request.InsuranceValue),
            items = new[]
            {
                new
                {
                    name = string.IsNullOrWhiteSpace(request.ItemName) ? "Nông sản FreshFarm" : request.ItemName.Trim(),
                    quantity = Math.Max(1, request.ItemQuantity),
                    height = Math.Max(1, request.Height),
                    length = Math.Max(1, request.Length),
                    width = Math.Max(1, request.Width),
                    weight = Math.Max(1, request.Weight)
                }
            }
        });

        using var feeResponse = await client.SendAsync(feeRequest, cancellationToken);
        var feePayload = await feeResponse.Content.ReadFromJsonAsync<GhnFeeEnvelope>(cancellationToken: cancellationToken);
        if (!feeResponse.IsSuccessStatusCode || feePayload?.Data is null)
        {
            var message = feePayload?.Message;
            return new GhnSandboxFeeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                ServiceId = service.ServiceId,
                ServiceName = service.ShortName,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"GHN tính phí trả về HTTP {(int)feeResponse.StatusCode}."
                    : $"GHN tính phí trả lỗi: {message}"
            };
        }

        return new GhnSandboxFeeResult
        {
            Success = true,
            ShopId = _options.ShopId,
            ServiceId = service.ServiceId,
            ServiceName = service.ShortName,
            TotalFee = feePayload.Data.Total,
            MainServiceFee = feePayload.Data.ServiceFee,
            InsuranceFee = feePayload.Data.InsuranceFee,
            FromDistrictId = origin.Value.FromDistrictId,
            FromWardCode = origin.Value.FromWardCode,
            Message = "Tính phí GHN sandbox thành công."
        };
    }

    public async Task<GhnSandboxLeadTimeResult> CalculateLeadTimeAsync(GhnSandboxLeadTimeRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GhnSandboxLeadTimeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa cấu hình đầy đủ GHN sandbox. Cần `BaseUrl`, `Token` và `ShopId`."
            };
        }

        if (!_options.ShopId.HasValue || _options.ShopId.Value <= 0)
        {
            return new GhnSandboxLeadTimeResult
            {
                Success = false,
                Message = "Chưa có `ShopId` GHN hợp lệ."
            };
        }

        if (request.ToDistrictId <= 0 || string.IsNullOrWhiteSpace(request.ToWardCode))
        {
            return new GhnSandboxLeadTimeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Thiếu quận/huyện hoặc phường/xã nhận hàng để tính leadtime."
            };
        }

        var origin = await ResolveOriginAsync(request.OriginOverride, cancellationToken);
        if (origin is null)
        {
            return new GhnSandboxLeadTimeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa xác định được địa chỉ gửi GHN. Hãy kiểm tra lại shop sandbox hoặc cấu hình `FromDistrictId`, `FromWardCode`."
            };
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        var service = await ResolveServiceAsync(client, new GhnSandboxFeeRequest
        {
            ToDistrictId = request.ToDistrictId,
            ToWardCode = request.ToWardCode,
            ServiceTypeId = request.ServiceTypeId,
            OriginOverride = request.OriginOverride
        }, origin.Value.FromDistrictId, cancellationToken);

        if (service is null)
        {
            return new GhnSandboxLeadTimeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Không tìm được dịch vụ GHN phù hợp để lấy leadtime."
            };
        }

        using var leadTimeRequest = CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/leadtime", new
        {
            from_district_id = origin.Value.FromDistrictId,
            from_ward_code = origin.Value.FromWardCode,
            to_district_id = request.ToDistrictId,
            to_ward_code = request.ToWardCode.Trim(),
            service_id = service.ServiceId
        });

        using var leadTimeResponse = await client.SendAsync(leadTimeRequest, cancellationToken);
        var leadTimePayload = await leadTimeResponse.Content.ReadFromJsonAsync<GhnLeadTimeEnvelope>(cancellationToken: cancellationToken);
        if (!leadTimeResponse.IsSuccessStatusCode || leadTimePayload?.Data is null)
        {
            var message = leadTimePayload?.Message;
            return new GhnSandboxLeadTimeResult
            {
                Success = false,
                ShopId = _options.ShopId,
                ServiceId = service.ServiceId,
                ServiceName = service.ShortName,
                FromDistrictId = origin.Value.FromDistrictId,
                FromWardCode = origin.Value.FromWardCode,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"GHN leadtime trả về HTTP {(int)leadTimeResponse.StatusCode}."
                    : $"GHN leadtime trả lỗi: {message}"
            };
        }

        return new GhnSandboxLeadTimeResult
        {
            Success = true,
            ShopId = _options.ShopId,
            ServiceId = service.ServiceId,
            ServiceName = service.ShortName,
            FromDistrictId = origin.Value.FromDistrictId,
            FromWardCode = origin.Value.FromWardCode,
            LeadTimeUnix = leadTimePayload.Data.LeadTime,
            LeadTime = ToDateTimeOffset(leadTimePayload.Data.LeadTime),
            OrderDateUnix = leadTimePayload.Data.OrderDate,
            OrderDate = ToDateTimeOffset(leadTimePayload.Data.OrderDate),
            Message = "Lấy thời gian giao dự kiến GHN sandbox thành công."
        };
    }

    public async Task<GhnSandboxCreateOrderResult> CreateOrderAsync(GhnSandboxCreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa cấu hình đầy đủ GHN sandbox. Cần `BaseUrl`, `Token` và `ShopId`."
            };
        }

        if (!_options.ShopId.HasValue || _options.ShopId.Value <= 0)
        {
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                Message = "Chưa có `ShopId` GHN hợp lệ."
            };
        }

        if (request.ToDistrictId <= 0 || string.IsNullOrWhiteSpace(request.ToWardCode) || string.IsNullOrWhiteSpace(request.ToName) || string.IsNullOrWhiteSpace(request.ToPhone) || string.IsNullOrWhiteSpace(request.ToAddress))
        {
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Thiếu thông tin người nhận hoặc địa chỉ nhận hàng để tạo đơn GHN."
            };
        }

        var origin = await ResolveOriginAsync(request.OriginOverride, cancellationToken);
        if (origin is null)
        {
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa xác định được địa chỉ gửi GHN. Hãy kiểm tra lại shop sandbox hoặc cấu hình `FromDistrictId`, `FromWardCode`."
            };
        }

        var shopProfile = await GetShopProfileAsync(cancellationToken);
        var returnPhone = !string.IsNullOrWhiteSpace(request.OriginOverride?.ReturnPhone)
            ? request.OriginOverride.ReturnPhone.Trim()
            : !string.IsNullOrWhiteSpace(_options.ReturnPhone)
            ? _options.ReturnPhone.Trim()
            : shopProfile.Phone;
        var returnAddress = !string.IsNullOrWhiteSpace(request.OriginOverride?.ReturnAddress)
            ? request.OriginOverride.ReturnAddress.Trim()
            : !string.IsNullOrWhiteSpace(_options.ReturnAddress)
            ? _options.ReturnAddress.Trim()
            : shopProfile.Address;
        if (string.IsNullOrWhiteSpace(returnPhone) || string.IsNullOrWhiteSpace(returnAddress))
        {
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa có đủ thông tin địa chỉ hoàn hàng. Hãy cấu hình `ReturnPhone` và `ReturnAddress` cho GHN sandbox."
            };
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        var service = await ResolveServiceAsync(client, new GhnSandboxFeeRequest
        {
            ToDistrictId = request.ToDistrictId,
            ToWardCode = request.ToWardCode,
            ServiceTypeId = request.ServiceTypeId,
            OriginOverride = request.OriginOverride
        }, origin.Value.FromDistrictId, cancellationToken);

        if (service is null)
        {
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Không tìm được dịch vụ GHN phù hợp để tạo đơn."
            };
        }

        var normalizedItems = request.Items.Count > 0
            ? request.Items
            : new List<GhnSandboxOrderItemRequest>
            {
                new()
                {
                    Name = "Nông sản FreshFarm",
                    Quantity = 1,
                    Price = Math.Max(0, request.CodAmount),
                    Height = request.Height,
                    Length = request.Length,
                    Width = request.Width,
                    Weight = request.Weight
                }
            };

        var clientOrderCode = string.IsNullOrWhiteSpace(request.ClientOrderCode)
            ? $"FF-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
            : request.ClientOrderCode.Trim();

        using var createOrderRequest = CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/create", new
        {
            payment_type_id = request.PaymentTypeId > 0 ? request.PaymentTypeId : 2,
            note = request.Note?.Trim() ?? string.Empty,
            required_note = string.IsNullOrWhiteSpace(request.RequiredNote) ? "KHONGCHOXEMHANG" : request.RequiredNote.Trim(),
            return_phone = returnPhone,
            return_address = returnAddress,
            return_district_id = origin.Value.FromDistrictId,
            return_ward_code = origin.Value.FromWardCode,
            client_order_code = clientOrderCode,
            to_name = request.ToName.Trim(),
            to_phone = request.ToPhone.Trim(),
            to_address = request.ToAddress.Trim(),
            to_ward_code = request.ToWardCode.Trim(),
            to_district_id = request.ToDistrictId,
            cod_amount = Math.Max(0, request.CodAmount),
            content = string.IsNullOrWhiteSpace(request.Content) ? "Đơn hàng thử nghiệm FreshFarm" : request.Content.Trim(),
            weight = Math.Max(1, request.Weight),
            length = Math.Max(1, request.Length),
            width = Math.Max(1, request.Width),
            height = Math.Max(1, request.Height),
            insurance_value = Math.Max(0, request.InsuranceValue),
            service_id = service.ServiceId,
            items = normalizedItems.Select(item => new
            {
                name = string.IsNullOrWhiteSpace(item.Name) ? "Nông sản FreshFarm" : item.Name.Trim(),
                code = item.Code?.Trim() ?? string.Empty,
                quantity = Math.Max(1, item.Quantity),
                price = Math.Max(0, item.Price),
                length = Math.Max(1, item.Length),
                width = Math.Max(1, item.Width),
                height = Math.Max(1, item.Height),
                weight = Math.Max(1, item.Weight)
            }).ToArray()
        });

        using var createOrderResponse = await client.SendAsync(createOrderRequest, cancellationToken);
        var createOrderPayload = await createOrderResponse.Content.ReadFromJsonAsync<GhnCreateOrderEnvelope>(cancellationToken: cancellationToken);
        if (!createOrderResponse.IsSuccessStatusCode || createOrderPayload?.Data is null)
        {
            var message = createOrderPayload?.Message;
            return new GhnSandboxCreateOrderResult
            {
                Success = false,
                ShopId = _options.ShopId,
                ClientOrderCode = clientOrderCode,
                ServiceId = service.ServiceId,
                ServiceName = service.ShortName,
                FromDistrictId = origin.Value.FromDistrictId,
                FromWardCode = origin.Value.FromWardCode,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"GHN tạo đơn trả về HTTP {(int)createOrderResponse.StatusCode}."
                    : $"GHN tạo đơn trả lỗi: {message}"
            };
        }

        return new GhnSandboxCreateOrderResult
        {
            Success = true,
            ShopId = _options.ShopId,
            ClientOrderCode = clientOrderCode,
            OrderCode = createOrderPayload.Data.OrderCode?.Trim() ?? string.Empty,
            SortCode = createOrderPayload.Data.SortCode?.Trim() ?? string.Empty,
            ServiceId = service.ServiceId,
            ServiceName = service.ShortName,
            TotalFee = createOrderPayload.Data.TotalFee,
            ExpectedDeliveryTimeUnix = ToUnixTime(createOrderPayload.Data.ExpectedDeliveryTimeRaw),
            ExpectedDeliveryTime = ToDateTimeOffset(createOrderPayload.Data.ExpectedDeliveryTimeRaw),
            FromDistrictId = origin.Value.FromDistrictId,
            FromWardCode = origin.Value.FromWardCode,
            Message = "Tạo đơn GHN sandbox thành công."
        };
    }

    public async Task<GhnSandboxOrderTrackingResult> GetOrderTrackingAsync(GhnSandboxOrderTrackingRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GhnSandboxOrderTrackingResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Chưa cấu hình đầy đủ GHN sandbox. Cần `BaseUrl`, `Token` và `ShopId`."
            };
        }

        if (!_options.ShopId.HasValue || _options.ShopId.Value <= 0)
        {
            return new GhnSandboxOrderTrackingResult
            {
                Success = false,
                Message = "Chưa có `ShopId` GHN hợp lệ."
            };
        }

        if (string.IsNullOrWhiteSpace(request.OrderCode) && string.IsNullOrWhiteSpace(request.ClientOrderCode))
        {
            return new GhnSandboxOrderTrackingResult
            {
                Success = false,
                ShopId = _options.ShopId,
                Message = "Cần cung cấp `orderCode` hoặc `clientOrderCode` để tra cứu GHN."
            };
        }

        var client = _httpClientFactory.CreateClient("GhnSandbox");
        using var trackingRequest = CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/detail", new
        {
            order_code = request.OrderCode?.Trim() ?? string.Empty,
            client_order_code = request.ClientOrderCode?.Trim() ?? string.Empty
        });

        using var trackingResponse = await client.SendAsync(trackingRequest, cancellationToken);
        var trackingPayload = await trackingResponse.Content.ReadFromJsonAsync<GhnOrderDetailEnvelope>(cancellationToken: cancellationToken);
        if (!trackingResponse.IsSuccessStatusCode || trackingPayload?.Data is null)
        {
            var message = trackingPayload?.Message;
            return new GhnSandboxOrderTrackingResult
            {
                Success = false,
                ShopId = _options.ShopId,
                OrderCode = request.OrderCode?.Trim() ?? string.Empty,
                ClientOrderCode = request.ClientOrderCode?.Trim() ?? string.Empty,
                Message = string.IsNullOrWhiteSpace(message)
                    ? $"GHN tra cứu đơn trả về HTTP {(int)trackingResponse.StatusCode}."
                    : $"GHN tra cứu đơn trả lỗi: {message}"
            };
        }

        return new GhnSandboxOrderTrackingResult
        {
            Success = true,
            ShopId = _options.ShopId,
            OrderCode = trackingPayload.Data.OrderCode?.Trim() ?? string.Empty,
            ClientOrderCode = trackingPayload.Data.ClientOrderCode?.Trim() ?? string.Empty,
            Status = trackingPayload.Data.Status?.Trim() ?? string.Empty,
            StatusLabel = TranslateStatus(trackingPayload.Data.Status),
            ServiceName = trackingPayload.Data.ServiceTypeName?.Trim() ?? string.Empty,
            FromName = trackingPayload.Data.FromName?.Trim() ?? string.Empty,
            FromPhone = trackingPayload.Data.FromPhone?.Trim() ?? string.Empty,
            FromAddress = BuildAddress(trackingPayload.Data.FromAddress, trackingPayload.Data.FromWardName, trackingPayload.Data.FromDistrictName, trackingPayload.Data.FromProvinceName),
            ToName = trackingPayload.Data.ToName?.Trim() ?? string.Empty,
            ToPhone = trackingPayload.Data.ToPhone?.Trim() ?? string.Empty,
            ToAddress = BuildAddress(trackingPayload.Data.ToAddress, trackingPayload.Data.WardName, trackingPayload.Data.DistrictName, trackingPayload.Data.ProvinceName),
            CurrentWarehouseId = trackingPayload.Data.CurrentWarehouseId ?? ExtractWarehouseId(trackingPayload.Data.CurrentWarehouseRaw),
            CurrentWarehouseName = ExtractWarehouseName(trackingPayload.Data.CurrentWarehouseName, trackingPayload.Data.CurrentWarehouseRaw),
            Weight = trackingPayload.Data.Weight,
            Length = trackingPayload.Data.Length,
            Width = trackingPayload.Data.Width,
            Height = trackingPayload.Data.Height,
            CodAmount = trackingPayload.Data.CodAmount,
            TotalFee = trackingPayload.Data.TotalFee,
            CreatedDate = ToDateTimeOffset(trackingPayload.Data.CreatedDateRaw),
            LeadTime = ToDateTimeOffset(trackingPayload.Data.LeadtimeRaw),
            FinishedDate = ToDateTimeOffset(trackingPayload.Data.FinishDateRaw),
            Logs = (trackingPayload.Data.Logs ?? new List<GhnOrderLogItem>())
                .Select(log => new GhnSandboxOrderTrackingLogItem
                {
                    Status = log.Status?.Trim() ?? string.Empty,
                    StatusLabel = TranslateStatus(log.Status),
                    UpdatedAt = ToDateTimeOffset(log.UpdatedDateRaw),
                    WarehouseId = log.WarehouseId ?? ExtractWarehouseId(log.WarehouseRaw),
                    WarehouseName = ExtractWarehouseName(log.WarehouseName, log.WarehouseRaw)
                })
                .ToList(),
            Message = "Tra cứu đơn GHN sandbox thành công."
        };
    }

    private async Task<GhnServiceItem?> ResolveServiceAsync(HttpClient client, GhnSandboxFeeRequest request, int fromDistrictId, CancellationToken cancellationToken)
    {
        using var serviceRequest = CreateRequest(HttpMethod.Post, "/shiip/public-api/v2/shipping-order/available-services", new
        {
            shop_id = _options.ShopId!.Value,
            from_district = fromDistrictId,
            to_district = request.ToDistrictId
        });

        using var serviceResponse = await client.SendAsync(serviceRequest, cancellationToken);
        var servicePayload = await serviceResponse.Content.ReadFromJsonAsync<GhnServiceEnvelope>(cancellationToken: cancellationToken);
        if (!serviceResponse.IsSuccessStatusCode || servicePayload?.Data is null || servicePayload.Data.Count == 0)
        {
            _logger.LogWarning("Không lấy được dịch vụ GHN khả dụng. HTTP {StatusCode}.", (int)serviceResponse.StatusCode);
            return null;
        }

        var requestedServiceTypeId = request.ServiceTypeId.GetValueOrDefault();
        var selected = requestedServiceTypeId > 0
            ? servicePayload.Data.FirstOrDefault(x => x.ServiceTypeId == requestedServiceTypeId)
            : null;

        selected ??= servicePayload.Data
            .OrderBy(x => x.ServiceTypeId == 2 ? 0 : 1)
            .ThenBy(x => x.ServiceId)
            .FirstOrDefault();

        return selected;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Token", _options.Token);
        if (_options.ShopId.HasValue && _options.ShopId.Value > 0)
        {
            request.Headers.Add("ShopId", _options.ShopId.Value.ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<(int FromDistrictId, string FromWardCode)?> ResolveOriginAsync(GhnSandboxOriginOverride? originOverride, CancellationToken cancellationToken)
    {
        var overrideDistrictId = originOverride?.FromDistrictId.GetValueOrDefault() ?? 0;
        var overrideWardCode = originOverride?.FromWardCode?.Trim() ?? string.Empty;
        if (overrideDistrictId > 0 && !string.IsNullOrWhiteSpace(overrideWardCode))
        {
            return (overrideDistrictId, overrideWardCode);
        }

        var configuredDistrictId = _options.FromDistrictId.GetValueOrDefault();
        var configuredWardCode = _options.FromWardCode?.Trim() ?? string.Empty;
        if (configuredDistrictId > 0 && !string.IsNullOrWhiteSpace(configuredWardCode))
        {
            return (configuredDistrictId, configuredWardCode);
        }

        var shopProfile = await GetShopProfileAsync(cancellationToken);
        if (!shopProfile.Success || !shopProfile.DistrictId.HasValue || shopProfile.DistrictId.Value <= 0 || string.IsNullOrWhiteSpace(shopProfile.WardCode))
        {
            return null;
        }

        return (shopProfile.DistrictId.Value, shopProfile.WardCode);
    }

    private static string BuildAddress(params string?[] parts)
    {
        return string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
    }

    private static long? ToUnixTime(JsonElement rawValue)
    {
        if (rawValue.ValueKind == JsonValueKind.Null || rawValue.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (rawValue.ValueKind == JsonValueKind.Number && rawValue.TryGetInt64(out var unixFromNumber))
        {
            return unixFromNumber;
        }

        if (rawValue.ValueKind == JsonValueKind.String)
        {
            var rawText = rawValue.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return null;
            }

            if (long.TryParse(rawText, out var unixFromString))
            {
                return unixFromString;
            }

            if (DateTimeOffset.TryParse(rawText, out var dateTimeOffset))
            {
                return dateTimeOffset.ToUnixTimeSeconds();
            }
        }

        return null;
    }

    private static DateTimeOffset? ToDateTimeOffset(JsonElement rawValue)
    {
        if (rawValue.ValueKind == JsonValueKind.String)
        {
            var rawText = rawValue.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(rawText) && DateTimeOffset.TryParse(rawText, out var parsedDateTime))
            {
                return parsedDateTime;
            }
        }

        return ToDateTimeOffset(ToUnixTime(rawValue));
    }

    private static DateTimeOffset? ToDateTimeOffset(long? unixTime)
    {
        if (!unixTime.HasValue || unixTime.Value <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixTime.Value);
    }

    private static int? ExtractWarehouseId(JsonElement warehouse)
    {
        if (warehouse.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "warehouse_id", "id", "WarehouseID" })
        {
            if (warehouse.TryGetProperty(propertyName, out var value))
            {
                var id = ToInt(value);
                if (id.HasValue)
                {
                    return id;
                }
            }
        }

        return null;
    }

    private static string ExtractWarehouseName(string? directName, JsonElement warehouse)
    {
        if (!string.IsNullOrWhiteSpace(directName))
        {
            return directName.Trim();
        }

        if (warehouse.ValueKind == JsonValueKind.String)
        {
            return warehouse.GetString()?.Trim() ?? string.Empty;
        }

        if (warehouse.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var propertyName in new[] { "warehouse_name", "name", "WarehouseName" })
        {
            if (warehouse.TryGetProperty(propertyName, out var value))
            {
                var name = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
        }

        return string.Empty;
    }

    private static int? ToInt(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private static string TranslateStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Chưa rõ trạng thái";
        }

        return TrackingStatusLabels.TryGetValue(status.Trim(), out var label)
            ? label
            : status.Trim();
    }

    private sealed class GhnProvinceEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public List<GhnProvinceItem>? Data { get; set; }
    }

    private sealed class GhnProvinceItem
    {
        [JsonPropertyName("ProvinceID")]
        public int ProvinceID { get; set; }

        [JsonPropertyName("ProvinceName")]
        public string? ProvinceName { get; set; }
    }

    private sealed class GhnDistrictEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public List<GhnDistrictItem>? Data { get; set; }
    }

    private sealed class GhnDistrictItem
    {
        [JsonPropertyName("DistrictID")]
        public int DistrictId { get; set; }

        [JsonPropertyName("DistrictName")]
        public string? DistrictName { get; set; }
    }

    private sealed class GhnWardEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public List<GhnWardItem>? Data { get; set; }
    }

    private sealed class GhnWardItem
    {
        [JsonPropertyName("WardCode")]
        public string? WardCode { get; set; }

        [JsonPropertyName("WardName")]
        public string? WardName { get; set; }
    }

    private sealed class GhnShopEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public GhnShopData? Data { get; set; }
    }

    private sealed class GhnShopData
    {
        [JsonPropertyName("shops")]
        public List<GhnShopItem>? Shops { get; set; }
    }

    private sealed class GhnShopItem
    {
        [JsonPropertyName("shop_id")]
        public int ShopId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("ward_code")]
        public string? WardCode { get; set; }

        [JsonPropertyName("ward_name")]
        public string? WardName { get; set; }

        [JsonPropertyName("district_id")]
        public int? DistrictId { get; set; }

        [JsonPropertyName("district_name")]
        public string? DistrictName { get; set; }

        [JsonPropertyName("province_name")]
        public string? ProvinceName { get; set; }
    }

    private sealed class GhnServiceEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public List<GhnServiceItem>? Data { get; set; }
    }

    private sealed class GhnServiceItem
    {
        [JsonPropertyName("service_id")]
        public int ServiceId { get; set; }

        [JsonPropertyName("service_type_id")]
        public int ServiceTypeId { get; set; }

        [JsonPropertyName("short_name")]
        public string ShortName { get; set; } = string.Empty;
    }

    private sealed class GhnFeeEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public GhnFeeData? Data { get; set; }
    }

    private sealed class GhnFeeData
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("service_fee")]
        public int ServiceFee { get; set; }

        [JsonPropertyName("insurance_fee")]
        public int InsuranceFee { get; set; }
    }

    private sealed class GhnLeadTimeEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public GhnLeadTimeData? Data { get; set; }
    }

    private sealed class GhnLeadTimeData
    {
        [JsonPropertyName("leadtime")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long? LeadTime { get; set; }

        [JsonPropertyName("order_date")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long? OrderDate { get; set; }
    }

    private sealed class GhnCreateOrderEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public GhnCreateOrderData? Data { get; set; }
    }

    private sealed class GhnCreateOrderData
    {
        [JsonPropertyName("order_code")]
        public string? OrderCode { get; set; }

        [JsonPropertyName("sort_code")]
        public string? SortCode { get; set; }

        [JsonPropertyName("total_fee")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? TotalFee { get; set; }

        [JsonPropertyName("expected_delivery_time")]
        public JsonElement ExpectedDeliveryTimeRaw { get; set; }
    }

    private sealed class GhnOrderDetailEnvelope
    {
        public int Code { get; set; }

        public string? Message { get; set; }

        public GhnOrderDetailData? Data { get; set; }
    }

    private sealed class GhnOrderDetailData
    {
        [JsonPropertyName("order_code")]
        public string? OrderCode { get; set; }

        [JsonPropertyName("client_order_code")]
        public string? ClientOrderCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("service_type_name")]
        public string? ServiceTypeName { get; set; }

        [JsonPropertyName("from_name")]
        public string? FromName { get; set; }

        [JsonPropertyName("from_phone")]
        public string? FromPhone { get; set; }

        [JsonPropertyName("from_address")]
        public string? FromAddress { get; set; }

        [JsonPropertyName("from_ward_name")]
        public string? FromWardName { get; set; }

        [JsonPropertyName("from_district_name")]
        public string? FromDistrictName { get; set; }

        [JsonPropertyName("from_province_name")]
        public string? FromProvinceName { get; set; }

        [JsonPropertyName("to_name")]
        public string? ToName { get; set; }

        [JsonPropertyName("to_phone")]
        public string? ToPhone { get; set; }

        [JsonPropertyName("to_address")]
        public string? ToAddress { get; set; }

        [JsonPropertyName("ward_name")]
        public string? WardName { get; set; }

        [JsonPropertyName("district_name")]
        public string? DistrictName { get; set; }

        [JsonPropertyName("province_name")]
        public string? ProvinceName { get; set; }

        [JsonPropertyName("current_warehouse_id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? CurrentWarehouseId { get; set; }

        [JsonPropertyName("current_warehouse_name")]
        public string? CurrentWarehouseName { get; set; }

        [JsonPropertyName("current_warehouse")]
        public JsonElement CurrentWarehouseRaw { get; set; }

        [JsonPropertyName("weight")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? Weight { get; set; }

        [JsonPropertyName("length")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? Length { get; set; }

        [JsonPropertyName("width")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? Height { get; set; }

        [JsonPropertyName("cod_amount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? CodAmount { get; set; }

        [JsonPropertyName("total_fee")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? TotalFee { get; set; }

        [JsonPropertyName("created_date")]
        public JsonElement CreatedDateRaw { get; set; }

        [JsonPropertyName("leadtime")]
        public JsonElement LeadtimeRaw { get; set; }

        [JsonPropertyName("finish_date")]
        public JsonElement FinishDateRaw { get; set; }

        [JsonPropertyName("log")]
        public List<GhnOrderLogItem>? Logs { get; set; }
    }

    private sealed class GhnOrderLogItem
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("updated_date")]
        public JsonElement UpdatedDateRaw { get; set; }

        [JsonPropertyName("warehouse_id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? WarehouseId { get; set; }

        [JsonPropertyName("warehouse_name")]
        public string? WarehouseName { get; set; }

        [JsonPropertyName("warehouse")]
        public JsonElement WarehouseRaw { get; set; }
    }
}
