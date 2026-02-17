using FreshFarm.Ordering.Api.Dtos; // Doc cac request/response DTO vua tao.
using FreshFarm.Ordering.Api.Models; // Dung entity va DbContext scaffold tu DB.
using Microsoft.AspNetCore.Authorization; // Dung [Authorize] de bat buoc dang nhap.
using Microsoft.AspNetCore.Mvc; // Kieu ControllerBase va IActionResult.
using Microsoft.EntityFrameworkCore; // Dung async query + transaction cua EF Core.
using System.IdentityModel.Tokens.Jwt; // Doc claim `sub` tu JWT.
using System.Security.Claims; // Ho tro thao tac claim.

namespace FreshFarm.Ordering.Api.Controllers; // Namespace cua controller Ordering.

[ApiController] // Bat model validation tu dong va binding API chuan.
[Route("api/orders")] // Dat route ro rang, de endpoint on dinh.
[Authorize] // Moi endpoint trong controller nay deu can token hop le.
public sealed class OrdersController : ControllerBase // Dung ControllerBase cho API, khong dung Controller MVC.
{
    private readonly FreshFarmOrderingDBContext _db; // DbContext de truy cap CSDL Ordering.

    
    public OrdersController(FreshFarmOrderingDBContext db) // Inject DbContext qua DI.
    {
        _db = db; // Gan context vao field de dung trong action.
    }

    [HttpGet("my")] // Endpoint lay danh sach don cua chinh user dang dang nhap.
    public async Task<IActionResult> GetMyOrders(CancellationToken cancellationToken) // Async + cancellation cho API production-safe.
    {
        var userId = TryGetUserIdFromToken(); // Rut UserId tu JWT.
        if (userId is null) // Neu khong rut duoc user id thi token dang khong dung format app.
        {
            return Unauthorized("Token khong co claim user id hop le."); // Tra 401 de client xu ly login lai.
        }

        var data = await _db.Orders // Query tu bang Orders.
            .AsNoTracking() // Read-only nen tat tracking de giam memory/CPU.
            .Where(order => order.UserId == userId.Value) // Chi lay don cua user hien tai. //order là alias cho Order trong db.Orders, có thể đặtt tên khác, và oorrder là một row trong bảng Orders và có trường UserId
            .OrderByDescending(order => order.OrderDate) // Don moi nhat hien truoc.
            .Select(order => new OrderListItemResponse // Map sang response DTO gon nhe.
            {
                OrderId = order.OrderId, // Ma don.
                OrderDate = order.OrderDate, // Ngay tao.
                TotalAmount = order.TotalAmount, // Tong tien.
                Status = order.Status, // Trang thai don.
                PaymentStatus = order.PaymentStatus // Trang thai thanh toan.
            })
            .ToListAsync(cancellationToken); // Chay query async.

        return Ok(data); // Tra danh sach don cua user.
    }

    [HttpGet("{id:int}")] // Endpoint lay chi tiet 1 don theo id.
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken) // Nhan id tu route.
    {
        var userId = TryGetUserIdFromToken(); // Lay user id tu token.
        if (userId is null) // Token khong hop le cho app.
        {
            return Unauthorized("Token khong co claim user id hop le."); // 401.
        }

        var result = await _db.Orders // Query bang Orders.
            .AsNoTracking() // Chi doc, khong can track.
            .Where(o => o.OrderId == id) // Loc theo id don.
            .Select(o => new // Projection de tranh tra truc tiep entity EF.
            {
                o.UserId, // Dung de check quyen so huu don.
                Data = new OrderDetailResponse // Map chi tiet sang response DTO.
                {
                    OrderId = o.OrderId,
                    OrderDate = o.OrderDate,
                    ShippingFee = o.ShippingFee,
                    CouponId = o.CouponId,
                    TotalAmount = o.TotalAmount,
                    OrderNote = o.OrderNote,
                    Status = o.Status ?? string.Empty,
                    PaymentStatus = o.PaymentStatus,
                    PaidAt = o.PaidAt,
                    BuyerFullName = o.BuyerFullName,
                    BuyerPhone = o.BuyerPhone,
                    BuyerEmail = o.BuyerEmail,
                    PointsEarned = o.PointsEarned,
                    PointsRedeemed = o.PointsRedeemed,
                    Items = o.OrderDetails // Danh sach item theo thu tu on dinh.
                        .OrderBy(x => x.OrderDetailId)
                        .Select(x => new OrderDetailItemResponse
                        {
                            OrderDetailId = x.OrderDetailId,
                            ProductId = x.ProductId,
                            Quantity = x.Quantity,
                            UnitPrice = x.UnitPrice,
                            UnitSymbol = x.UnitSymbol
                        })
                        .ToList(),
                    Shippings = o.Shippings // Danh sach shipping theo thu tu on dinh.
                        .OrderBy(x => x.ShippingId)
                        .Select(x => new OrderDetailShippingResponse
                        {
                            ShippingId = x.ShippingId,
                            ShippingType = x.ShippingType,
                            FullName = x.FullName,
                            Phone = x.Phone,
                            Email = x.Email,
                            AddressDetail = x.AddressDetail,
                            ProvinceId = x.ProvinceId,
                            CommuneId = x.CommuneId
                        })
                        .ToList(),
                    Payments = o.Payments // Danh sach payment theo thu tu on dinh.
                        .OrderBy(x => x.PaymentId)
                        .Select(x => new OrderDetailPaymentResponse
                        {
                            PaymentId = x.PaymentId,
                            PaymentMethod = x.PaymentMethod,
                            BankName = x.BankName,
                            AccountName = x.AccountName,
                            AccountNumber = x.AccountNumber,
                            TransactionCode = x.TransactionCode,
                            PaymentStatus = x.PaymentStatus,
                            PaymentDate = x.PaymentDate
                        })
                        .ToList()
                }
            })
            .FirstOrDefaultAsync(cancellationToken); // Tim theo id don.

        if (result is null) // Khong tim thay don.
        {
            return NotFound($"Khong tim thay order id = {id}."); // 404 ro rang.
        }

        if (result.UserId != userId.Value) // User hien tai khong so huu don.
        {
            return Forbid(); // 403 de dam bao boundary du lieu.
        }

        return Ok(result.Data); // Tra response DTO de on dinh contract API va tranh cycle serialize.
    }

    [HttpPost] // Endpoint tao don moi.
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken) // Nhan request + cancellation token.
    {
        var userId = TryGetUserIdFromToken(); // Lay user id token.
        if (userId is null) // Token khong co claim sub hop le.
        {
            return Unauthorized("Token khong co claim user id hop le."); // 401.
        }

        if (request.Items.Count == 0) // Chan don rong, du da co MinLength.
        {
            return BadRequest("Order phai co it nhat 1 item."); // 400.
        }

        var invalidItem = request.Items.Any(item => item.Quantity <= 0 || item.UnitPrice < 0 || item.ProductId <= 0); // Validate nghiep vu co ban.
        if (invalidItem) // Co item khong hop le.
        {
            return BadRequest("Co item khong hop le (ProductId/Quantity/UnitPrice)."); // 400 ro rang.
        }

        var itemsAmount = request.Items.Sum(item => item.UnitPrice * item.Quantity); // Tinh tong tien hang.
        var shippingFee = request.ShippingFee ?? 0m; // Neu client khong gui thi mac dinh 0.
        var totalAmount = itemsAmount + shippingFee; // Tong tien don = hang + ship.

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken); // Mo transaction de giu du lieu nhat quan.
        try // Bat dau khoi tao don.
        {
            var order = new Order // Tao entity Orders.
            {
                UserId = userId.Value, // UserId den tu JWT, khong lay tu body de tranh fake user.
                OrderDate = DateTime.UtcNow, // Dong bo timezone theo server.
                ShippingFee = shippingFee, // Phi ship request.
                CouponId = null, // MVP chua xu ly coupon.
                TotalAmount = totalAmount, // Tong tien da tinh.
                OrderNote = request.OrderNote, // Ghi chu nguoi mua.
                Status = "Pending", // Trang thai khoi tao.
                PaymentStatus = "Pending", // Trang thai thanh toan khoi tao.
                PaidAt = null, // Chua thanh toan.
                StatusId = null, // MVP chua map status dictionary.
                BuyerFullName = request.Shipping.FullName, // Snapshot nguoi nhan vao bang Orders.
                BuyerPhone = request.Shipping.Phone, // Snapshot SDT.
                BuyerEmail = request.Shipping.Email, // Snapshot email.
                PointsEarned = 0, // MVP chua xu ly loyalty.
                PointsRedeemed = 0 // MVP chua xu ly redeem point.
            };

            _db.Orders.Add(order); // Add order vao context.
            await _db.SaveChangesAsync(cancellationToken); // Save lan 1 de lay `OrderId`.

            var details = request.Items.Select(item => new OrderDetail // Map item request sang OrderDetail entity.
            {
                OrderId = order.OrderId, // Gan FK den order vua tao.
                ProductId = item.ProductId, // Product id tu request.
                Quantity = item.Quantity, // So luong.
                UnitPrice = item.UnitPrice, // Gia snapshot tai luc dat.
                UnitSymbol = item.UnitSymbol // Don vi.
            }).ToList(); // Materialize list de AddRange.

            _db.OrderDetails.AddRange(details); // Add toan bo dong hang.

            var shipping = new Shipping // Tao ban ghi Shipping.
            {
                OrderId = order.OrderId, // FK den order.
                ShippingType = request.Shipping.ShippingType, // Kieu giao hang.
                FullName = request.Shipping.FullName, // Nguoi nhan.
                Phone = request.Shipping.Phone, // So dien thoai.
                Email = request.Shipping.Email, // Email.
                AddressDetail = request.Shipping.AddressDetail, // Dia chi.
                ProvinceId = request.Shipping.ProvinceId, // Optional province.
                CommuneId = request.Shipping.CommuneId // Optional commune.
            };

            _db.Shippings.Add(shipping); // Add thong tin giao nhan.

            if (request.Payment is not null) // Chi tao Payment neu client gui.
            {
                var payment = new Payment // Tao payment khoi tao.
                {
                    OrderId = order.OrderId, // FK den order.
                    PaymentMethod = request.Payment.PaymentMethod, // Phuong thuc thanh toan.
                    PaymentStatus = "Pending", // Trang thai ban dau.
                    PaymentDate = null, // Chua thanh toan.
                    UserId = userId.Value // Luu user de doi chieu.
                };

                _db.Payments.Add(payment); // Add payment vao context.
            }

            await _db.SaveChangesAsync(cancellationToken); // Save lan 2 cho detail/shipping/payment.
            await transaction.CommitAsync(cancellationToken); // Commit transaction khi tat ca thanh cong.

            return CreatedAtAction( // Tra 201 theo REST convention.
                nameof(GetById), // Link den endpoint GetById.
                new { id = order.OrderId }, // Route value cho URL moi tao.
                new { orderId = order.OrderId, totalAmount }); // Payload gon cho client.
        }
        catch (Exception ex) // Bat loi de rollback transaction.
        {
            await transaction.RollbackAsync(cancellationToken); // Rollback de tranh du lieu nua chung.
            return StatusCode(500, new { message = "Tao order that bai.", detail = ex.Message }); // Tra 500 + detail de debug local.
        }
    }

    private int? TryGetUserIdFromToken() // Helper lay UserId tu JWT.
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub) // Uu tien claim `sub` vi Identity dang phat claim nay.
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier); // Fallback cho truong hop token map sang NameIdentifier.

        if (!int.TryParse(sub, out var userId)) // Neu parse that bai thi token khong hop le cho schema user int.
        {
            return null; // Tra null de action xu ly 401.
        }

        return userId; // Tra user id hop le.
    }
}
