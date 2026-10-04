using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/admin/customers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "SellerOrAdmin")]
public sealed class AdminCustomersController : ControllerBase
{
    private const string UserNamePattern = @"^[a-zA-Z0-9._-]+$";
    private static readonly Regex PhoneRegex = new("^\\d{10}$", RegexOptions.Compiled);

    private readonly FreshFarmIdentityDBContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AdminCustomersController(FreshFarmIdentityDBContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? keyword = null,
        [FromQuery] int take = 1000,
        [FromQuery] int[]? userIds = null)
    {
        if (take <= 0)
        {
            take = 1000;
        }

        if (take > 5000)
        {
            take = 5000;
        }

        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.UserRoles.Any(ur => ur.Role.RoleName == "Customer"));

        var normalizedUserIds = (userIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .Take(5000)
            .ToArray();

        if (normalizedUserIds.Length > 0)
        {
            query = query.Where(u => normalizedUserIds.Contains(u.UserId));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim().ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                u.UserName.ToLower().Contains(term) ||
                (u.Phone != null && u.Phone.Contains(term)));
        }

        var data = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                avatar = u.Avatar,
                createdAt = u.CreatedAt,
                address = u.AddressBooks
                    .Where(a => a.IsActive)
                    .OrderByDescending(a => a.IsDefault)
                    .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                    .ThenByDescending(a => a.AddressId)
                    .Select(a => a.AddressDetail)
                    .FirstOrDefault()
            })
            .Take(take)
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == id && u.UserRoles.Any(ur => ur.Role.RoleName == "Customer"))
            .Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                avatar = u.Avatar,
                createdAt = u.CreatedAt,
                address = u.AddressBooks
                    .Where(a => a.IsActive)
                    .OrderByDescending(a => a.IsDefault)
                    .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                    .ThenByDescending(a => a.AddressId)
                    .Select(a => a.AddressDetail)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay khach hang." });
        }

        return Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateCustomerRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Du lieu khong hop le." });
        }

        var normalized = NormalizeCreateRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var duplicateEmail = await _db.Users.AnyAsync(u => u.Email == normalized.email);
        if (duplicateEmail)
        {
            return Conflict(new { message = "Email da duoc su dung boi tai khoan khac." });
        }

        var duplicateUserName = await _db.Users.AnyAsync(u => u.UserName == normalized.userName);
        if (duplicateUserName)
        {
            return Conflict(new { message = "Ten dang nhap da duoc su dung." });
        }

        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            var duplicatePhone = await _db.Users.AnyAsync(u => u.Phone == normalized.phone);
            if (duplicatePhone)
            {
                return Conflict(new { message = "So dien thoai da duoc su dung." });
            }
        }

        var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == "Customer" && r.IsActive);
        if (customerRole is null)
        {
            return BadRequest(new { message = "Khong tim thay role Customer." });
        }

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            var user = new User
            {
                UserName = normalized.userName,
                FullName = normalized.fullName,
                Email = normalized.email,
                Phone = normalized.phone,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            _db.UserAuths.Add(new UserAuth
            {
                UserId = user.UserId,
                PasswordHash = _passwordHasher.HashPassword(user, normalized.password),
                FailedCount = 0,
                UpdatedAt = DateTime.UtcNow
            });

            _db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = customerRole.RoleId,
                CreatedAt = DateTime.UtcNow
            });

            if (!string.IsNullOrWhiteSpace(normalized.address))
            {
                if (string.IsNullOrWhiteSpace(normalized.phone))
                {
                    return BadRequest(new { message = "Vui long nhap so dien thoai khi tao dia chi." });
                }

                _db.AddressBooks.Add(new AddressBook
                {
                    UserId = user.UserId,
                    RecipientName = normalized.fullName,
                    Phone = normalized.phone,
                    AddressDetail = normalized.address,
                    Province = null,
                    District = null,
                    Ward = null,
                    IsDefault = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();
            await tran.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Them khach hang thanh cong.",
                userId = user.UserId
            });
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return BadRequest(new { message = "Khong the them khach hang.", detail = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] AdminUpdateCustomerRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Du lieu khong hop le." });
        }

        var user = await _db.Users
            .Include(u => u.UserAuth)
            .Include(u => u.AddressBooks)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == id && u.UserRoles.Any(ur => ur.Role.RoleName == "Customer"));

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay khach hang." });
        }

        var normalized = NormalizeUpdateRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var duplicateEmail = await _db.Users.AnyAsync(u => u.UserId != id && u.Email == normalized.email);
        if (duplicateEmail)
        {
            return Conflict(new { message = "Email da duoc su dung boi tai khoan khac." });
        }

        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            var duplicatePhone = await _db.Users.AnyAsync(u => u.UserId != id && u.Phone == normalized.phone);
            if (duplicatePhone)
            {
                return Conflict(new { message = "So dien thoai da duoc su dung." });
            }
        }

        user.FullName = normalized.fullName;
        user.Email = normalized.email;
        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            user.Phone = normalized.phone;
        }

        user.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(normalized.newPassword))
        {
            if (user.UserAuth is null)
            {
                user.UserAuth = new UserAuth
                {
                    UserId = user.UserId,
                    FailedCount = 0,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            user.UserAuth.PasswordHash = _passwordHasher.HashPassword(user, normalized.newPassword);
            user.UserAuth.UpdatedAt = DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(normalized.address))
        {
            if (string.IsNullOrWhiteSpace(normalized.phone) && string.IsNullOrWhiteSpace(user.Phone))
            {
                return BadRequest(new { message = "Vui long nhap so dien thoai khi cap nhat dia chi." });
            }

            var targetAddress = user.AddressBooks
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt)
                .ThenByDescending(a => a.AddressId)
                .FirstOrDefault();

            if (targetAddress is null)
            {
                targetAddress = new AddressBook
                {
                    UserId = user.UserId,
                    RecipientName = user.FullName,
                    Phone = string.IsNullOrWhiteSpace(normalized.phone) ? user.Phone : normalized.phone,
                    AddressDetail = normalized.address,
                    Province = null,
                    District = null,
                    Ward = null,
                    IsDefault = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _db.AddressBooks.Add(targetAddress);
            }
            else
            {
                targetAddress.RecipientName = user.FullName;
                targetAddress.Phone = string.IsNullOrWhiteSpace(normalized.phone) ? user.Phone : normalized.phone;
                targetAddress.AddressDetail = normalized.address;
                targetAddress.UpdatedAt = DateTime.UtcNow;
                targetAddress.IsActive = true;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Cap nhat khach hang thanh cong." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var user = await _db.Users
            .Include(u => u.UserAuth)
            .Include(u => u.UserRoles)
            .Include(u => u.UserSessions)
            .Include(u => u.AddressBooks)
            .FirstOrDefaultAsync(u => u.UserId == id && u.UserRoles.Any(ur => ur.Role.RoleName == "Customer"));

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay khach hang." });
        }

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            if (user.AddressBooks.Count > 0)
            {
                _db.AddressBooks.RemoveRange(user.AddressBooks);
            }

            if (user.UserSessions.Count > 0)
            {
                _db.UserSessions.RemoveRange(user.UserSessions);
            }

            if (user.UserRoles.Count > 0)
            {
                _db.UserRoles.RemoveRange(user.UserRoles);
            }

            if (user.UserAuth is not null)
            {
                _db.UserAuths.Remove(user.UserAuth);
            }

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            await tran.CommitAsync();

            return Ok(new { success = true, message = "Xoa khach hang thanh cong." });
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return BadRequest(new { success = false, message = "Khong the xoa khach hang.", detail = ex.Message });
        }
    }

    private static (bool isValid, string error, string userName, string fullName, string email, string phone, string password, string address) NormalizeCreateRequest(AdminCreateCustomerRequest request)
    {
        var userName = (request.UserName ?? string.Empty).Trim();
        var fullName = (request.FullName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var phone = (request.Phone ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var address = (request.Address ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            return (false, "Ten dang nhap la bat buoc.", userName, fullName, email, phone, password, address);
        }

        if (!Regex.IsMatch(userName, UserNamePattern))
        {
            return (false, "Ten dang nhap chi duoc chua chu cai, so, dau cham, gach duoi hoac gach ngang.", userName, fullName, email, phone, password, address);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Ho va ten la bat buoc.", userName, fullName, email, phone, password, address);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email la bat buoc.", userName, fullName, email, phone, password, address);
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            return (false, "Mat khau toi thieu 6 ky tu.", userName, fullName, email, phone, password, address);
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return (false, "So dien thoai la bat buoc.", userName, fullName, email, phone, password, address);
        }

        if (!PhoneRegex.IsMatch(phone))
        {
            return (false, "So dien thoai phai du 10 so.", userName, fullName, email, phone, password, address);
        }

        return (true, string.Empty, userName, fullName, email, phone, password, address);
    }

    private static (bool isValid, string error, string fullName, string email, string phone, string newPassword, string address) NormalizeUpdateRequest(AdminUpdateCustomerRequest request)
    {
        var fullName = (request.FullName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var phone = (request.Phone ?? string.Empty).Trim();
        var newPassword = (request.NewPassword ?? string.Empty).Trim();
        var address = (request.Address ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Ho va ten la bat buoc.", fullName, email, phone, newPassword, address);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email la bat buoc.", fullName, email, phone, newPassword, address);
        }

        if (!string.IsNullOrWhiteSpace(phone) && !PhoneRegex.IsMatch(phone))
        {
            return (false, "So dien thoai phai du 10 so.", fullName, email, phone, newPassword, address);
        }

        if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 6)
        {
            return (false, "Mat khau moi toi thieu 6 ky tu.", fullName, email, phone, newPassword, address);
        }

        return (true, string.Empty, fullName, email, phone, newPassword, address);
    }

    public sealed class AdminCreateCustomerRequest
    {
        [RegularExpression(UserNamePattern, ErrorMessage = "Ten dang nhap chi duoc chua chu cai, so, dau cham, gach duoi hoac gach ngang.")]
        public string UserName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string Password { get; set; } = string.Empty;

        public string? Address { get; set; }
    }

    public sealed class AdminUpdateCustomerRequest
    {
        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Address { get; set; }

        public string? NewPassword { get; set; }
    }
}
