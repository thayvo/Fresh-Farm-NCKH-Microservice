using FreshFarm.Ordering.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FreshFarm.Ordering.Api.Controllers;

[ApiController]
[Route("api/orders/admin/shippings")]
[Authorize(Policy = "SellerOnly")]
public sealed class ShippingAdminController : ControllerBase
{
    private static readonly string[] PaidStatuses = { "Da thanh toan", "Đã thanh toán", "Hoan tat", "Hoàn tất" };

    private static readonly List<ProvinceOption> Provinces = new()
    {
        new ProvinceOption(1, "TP. Ho Chi Minh"),
        new ProvinceOption(2, "Ha Noi"),
        new ProvinceOption(3, "Da Nang")
    };

    private static readonly Dictionary<int, List<CommuneOption>> CommunesByProvince = new()
    {
        [1] = new List<CommuneOption>
        {
            new CommuneOption(101, "Phuong 1"),
            new CommuneOption(102, "Phuong 2")
        },
        [2] = new List<CommuneOption>
        {
            new CommuneOption(201, "Phuong A"),
            new CommuneOption(202, "Phuong B")
        },
        [3] = new List<CommuneOption>
        {
            new CommuneOption(301, "Phuong Hai Chau"),
            new CommuneOption(302, "Phuong Thanh Khe")
        }
    };

    private readonly FreshFarmOrderingDBContext _db;

    public ShippingAdminController(FreshFarmOrderingDBContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string status = "",
        [FromQuery] int? staffId = null,
        [FromQuery] string q = "",
        [FromQuery] string sort = "date_desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string deliveredState = "",
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var query = _db.Shippings
            .AsNoTracking()
            .Include(s => s.Order)
            .ThenInclude(o => o.Payments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Order.Status == status);
        }

        if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(deliveredState))
        {
            if (string.Equals(deliveredState, "pending", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s =>
                    s.Order.Status == "Delivered" &&
                    s.Order.Payments.Any(p => p.PaymentMethod == "COD") &&
                    s.Order.Payments.Any(p => p.PaymentMethod == "COD" &&
                        (!p.PaymentDate.HasValue &&
                         (p.PaymentStatus == null || !PaidStatuses.Contains(p.PaymentStatus)))));
            }
            else if (string.Equals(deliveredState, "done", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s =>
                    s.Order.Status == "Delivered" &&
                    s.Order.Payments.Any(p => p.PaymentMethod == "COD" &&
                        (p.PaymentDate.HasValue ||
                         (p.PaymentStatus != null && PaidStatuses.Contains(p.PaymentStatus)))));
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            if (int.TryParse(keyword, out var code))
            {
                query = query.Where(s => s.OrderId == code || s.ShippingId == code);
            }
            else
            {
                query = query.Where(s =>
                    s.FullName.Contains(keyword) ||
                    s.Phone.Contains(keyword) ||
                    (s.Email != null && s.Email.Contains(keyword)) ||
                    (s.AddressDetail != null && s.AddressDetail.Contains(keyword)) ||
                    (s.Order.BuyerFullName != null && s.Order.BuyerFullName.Contains(keyword)) ||
                    (s.Order.BuyerPhone != null && s.Order.BuyerPhone.Contains(keyword)));
            }
        }

        query = (sort ?? "date_desc").ToLowerInvariant() switch
        {
            "code_asc" => query.OrderBy(s => s.OrderId),
            "code_desc" => query.OrderByDescending(s => s.OrderId),
            "receiver_asc" => query.OrderBy(s => s.FullName),
            "receiver_desc" => query.OrderByDescending(s => s.FullName),
            "status_asc" => query.OrderBy(s => s.Order.Status),
            "status_desc" => query.OrderByDescending(s => s.Order.Status),
            "date_asc" => query.OrderBy(s => s.Order.OrderDate),
            _ => query.OrderByDescending(s => s.ShippingId)
        };

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                shippingID = s.ShippingId,
                orderID = s.OrderId,
                shippingType = s.ShippingType,
                fullName = s.FullName,
                phone = s.Phone,
                email = s.Email,
                addressDetail = s.AddressDetail,
                provinceId = s.ProvinceId,
                communeId = s.CommuneId,
                isStorePickup = s.ShippingType == "StorePickup",
                storeAddress = s.ShippingType == "StorePickup" ? s.AddressDetail : null,
                province = s.ProvinceId.HasValue
                    ? Provinces.Where(p => p.Id == s.ProvinceId.Value).Select(p => new { provinceId = p.Id, provinceName = p.Name }).FirstOrDefault()
                    : null,
                commune = s.CommuneId.HasValue && s.ProvinceId.HasValue && CommunesByProvince.ContainsKey(s.ProvinceId.Value)
                    ? CommunesByProvince[s.ProvinceId.Value].Where(c => c.Id == s.CommuneId.Value).Select(c => new { communeId = c.Id, communeName = c.Name }).FirstOrDefault()
                    : null,
                order = new
                {
                    orderID = s.Order.OrderId,
                    status = s.Order.Status,
                    payments = s.Order.Payments.Select(p => new
                    {
                        paymentID = p.PaymentId,
                        paymentMethod = p.PaymentMethod,
                        paymentStatus = p.PaymentStatus,
                        paymentDate = p.PaymentDate
                    })
                },
                deliveryAssignments = Array.Empty<object>()
            })
            .ToListAsync(cancellationToken);

        var orderIdsWithShipping = await _db.Shippings
            .AsNoTracking()
            .Select(s => s.OrderId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var ordersWithoutShipping = await _db.Orders
            .AsNoTracking()
            .Where(o => !orderIdsWithShipping.Contains(o.OrderId))
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new
            {
                orderID = o.OrderId,
                displayText = "Don hang #DH" + o.OrderId.ToString().PadLeft(5, '0')
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = new
            {
                items = rows,
                totalItems,
                totalPages,
                currentPage = page,
                pageSize,
                currentStatus = status,
                currentStaffId = staffId,
                currentQuery = q,
                currentSort = sort,
                deliveredState,
                provinces = Provinces.Select(p => new { provinceId = p.Id, provinceName = p.Name }),
                deliveryStaffs = Array.Empty<object>(),
                orders = ordersWithoutShipping
            }
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken cancellationToken)
    {
        var shipping = await _db.Shippings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShippingId == id, cancellationToken);

        if (shipping is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay thong tin van chuyen!" });
        }

        return Ok(new
        {
            success = true,
            shippingID = shipping.ShippingId,
            orderID = shipping.OrderId,
            shippingType = shipping.ShippingType,
            fullName = shipping.FullName,
            phone = shipping.Phone,
            email = shipping.Email,
            addressDetail = shipping.AddressDetail,
            provinceId = shipping.ProvinceId,
            communeId = shipping.CommuneId,
            isStorePickup = shipping.ShippingType == "StorePickup",
            storeAddress = shipping.ShippingType == "StorePickup" ? shipping.AddressDetail : null,
            deliveryStaffId = (int?)null
        });
    }

    [HttpGet("order-info/{orderId:int}")]
    public async Task<IActionResult> GetOrderInfo([FromRoute] int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        return Ok(new
        {
            success = true,
            fullName = order.BuyerFullName,
            phone = order.BuyerPhone,
            email = order.BuyerEmail,
            address = (string?)null,
            provinceId = (int?)null,
            communeId = (int?)null
        });
    }

    [HttpGet("communes")]
    public IActionResult GetCommunes([FromQuery] int provinceId)
    {
        if (!CommunesByProvince.TryGetValue(provinceId, out var communes))
        {
            communes = new List<CommuneOption>();
        }

        return Ok(communes.Select(c => new { value = c.Id, text = c.Name }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ShippingUpsertRequest request, CancellationToken cancellationToken)
    {
        if (request.OrderID <= 0)
        {
            return BadRequest(new { success = false, message = "Vui long chon don hang!" });
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten nguoi nhan!" });
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { success = false, message = "Vui long nhap so dien thoai!" });
        }

        var duplicate = await _db.Shippings.AnyAsync(s => s.OrderId == request.OrderID, cancellationToken);
        if (duplicate)
        {
            return BadRequest(new { success = false, message = "Don hang nay da co thong tin van chuyen!" });
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderId == request.OrderID, cancellationToken);
        if (order is null)
        {
            return BadRequest(new { success = false, message = "Don hang khong ton tai!" });
        }

        var (addressDetail, provinceId, communeId, shippingType) = NormalizeAddress(request);

        var shipping = new Shipping
        {
            OrderId = request.OrderID,
            ShippingType = shippingType,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            AddressDetail = addressDetail,
            ProvinceId = provinceId,
            CommuneId = communeId
        };

        _db.Shippings.Add(shipping);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Them thong tin van chuyen thanh cong!" });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] ShippingUpsertRequest request, CancellationToken cancellationToken)
    {
        var shipping = await _db.Shippings.FirstOrDefaultAsync(s => s.ShippingId == id, cancellationToken);
        if (shipping is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay thong tin van chuyen!" });
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { success = false, message = "Vui long nhap ten nguoi nhan!" });
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { success = false, message = "Vui long nhap so dien thoai!" });
        }

        var (addressDetail, provinceId, communeId, shippingType) = NormalizeAddress(request);

        shipping.ShippingType = shippingType;
        shipping.FullName = request.FullName.Trim();
        shipping.Phone = request.Phone.Trim();
        shipping.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        shipping.AddressDetail = addressDetail;
        shipping.ProvinceId = provinceId;
        shipping.CommuneId = communeId;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Cap nhat thong tin van chuyen thanh cong!" });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken cancellationToken)
    {
        var shipping = await _db.Shippings.FirstOrDefaultAsync(s => s.ShippingId == id, cancellationToken);
        if (shipping is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay thong tin van chuyen!" });
        }

        _db.Shippings.Remove(shipping);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Xoa thong tin van chuyen thanh cong!" });
    }

    [HttpPost("reconcile-cod")]
    public async Task<IActionResult> ReconcileCod([FromBody] ReconcileCodRequest request, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, cancellationToken);

        if (order is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        var cod = order.Payments.FirstOrDefault(p => p.PaymentMethod == "COD");
        if (cod is null)
        {
            return BadRequest(new { success = false, message = "Don nay chua co Payment COD de doi soat!" });
        }

        if (!cod.PaymentDate.HasValue)
        {
            cod.PaymentDate = DateTime.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(cod.PaymentStatus) || !PaidStatuses.Contains(cod.PaymentStatus))
        {
            cod.PaymentStatus = "Da thanh toan";
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da danh dau DA DOI SOAT (COD)." });
    }

    [HttpPost("unreconcile-cod")]
    public async Task<IActionResult> UnreconcileCod([FromBody] ReconcileCodRequest request, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, cancellationToken);

        if (order is null)
        {
            return NotFound(new { success = false, message = "Khong tim thay don hang!" });
        }

        var cod = order.Payments.FirstOrDefault(p => p.PaymentMethod == "COD");
        if (cod is null)
        {
            return BadRequest(new { success = false, message = "Don nay chua co Payment COD!" });
        }

        cod.PaymentDate = null;
        cod.PaymentStatus = "Chua thanh toan";

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { success = true, message = "Da BO doi soat (COD)." });
    }

    private static (string addressDetail, int? provinceId, int? communeId, string shippingType) NormalizeAddress(ShippingUpsertRequest request)
    {
        if (request.IsStorePickup)
        {
            var storeAddress = string.IsNullOrWhiteSpace(request.StoreAddress)
                ? "Nhan tai cua hang"
                : request.StoreAddress.Trim();

            return (storeAddress, null, null, "StorePickup");
        }

        var addressDetail = string.IsNullOrWhiteSpace(request.AddressDetail)
            ? ""
            : request.AddressDetail.Trim();

        var shippingType = string.IsNullOrWhiteSpace(request.ShippingType)
            ? "Giao noi bo"
            : request.ShippingType.Trim();

        return (addressDetail, request.ProvinceId, request.CommuneId, shippingType);
    }

    public sealed class ShippingUpsertRequest
    {
        public int OrderID { get; set; }

        public string? ShippingType { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string? AddressDetail { get; set; }

        public int? ProvinceId { get; set; }

        public int? CommuneId { get; set; }

        public bool IsStorePickup { get; set; }

        public string? StoreAddress { get; set; }

        public int? DeliveryStaffId { get; set; }
    }

    public sealed class ReconcileCodRequest
    {
        public int OrderId { get; set; }
    }

    private sealed record ProvinceOption(int Id, string Name);

    private sealed record CommuneOption(int Id, string Name);
}
