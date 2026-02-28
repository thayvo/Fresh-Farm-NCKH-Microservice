using FreshFarm.Identity.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FreshFarm.Identity.Api.Controllers;

[ApiController]
[Route("auth/admin/users")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "SellerOnly")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly FreshFarmIdentityDBContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AdminUsersController(FreshFarmIdentityDBContext db, IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search = null, [FromQuery] int take = 5000)
    {
        if (take <= 0)
        {
            take = 5000;
        }

        if (take > 10000)
        {
            take = 10000;
        }

        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.UserRoles.Any(ur => ur.Role.RoleName != "Customer"));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.UserName.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term) ||
                (u.Phone != null && u.Phone.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                avatar = u.Avatar,
                isActive = u.IsActive,
                created = u.CreatedAt,
                updated = u.UpdatedAt,
                lastLogin = u.UserSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                role = u.UserRoles
                    .Where(ur => ur.Role.RoleName != "Customer")
                    .OrderBy(ur => ur.Role.RoleName)
                    .Select(ur => new
                    {
                        roleId = ur.RoleId,
                        roleName = ur.Role.RoleName,
                        isActive = ur.Role.IsActive
                    })
                    .FirstOrDefault()
            })
            .Take(take)
            .ToListAsync();

        var result = rows.Select(row => new
        {
            row.userId,
            row.userName,
            row.fullName,
            row.email,
            row.phone,
            row.avatar,
            roleId = row.role?.roleId ?? 0,
            role = row.role,
            row.isActive,
            row.lastLogin,
            row.created,
            row.updated
        });

        return Ok(result);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.IsActive && r.RoleName != "Customer")
            .OrderBy(r => r.RoleName)
            .Select(r => new
            {
                roleId = r.RoleId,
                roleName = r.RoleName,
                isActive = r.IsActive
            })
            .ToListAsync();

        return Ok(roles);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        var row = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == id && u.UserRoles.Any(ur => ur.Role.RoleName != "Customer"))
            .Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                avatar = u.Avatar,
                isActive = u.IsActive,
                created = u.CreatedAt,
                updated = u.UpdatedAt,
                lastLogin = u.UserSessions
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefault(),
                role = u.UserRoles
                    .Where(ur => ur.Role.RoleName != "Customer")
                    .OrderBy(ur => ur.Role.RoleName)
                    .Select(ur => new
                    {
                        roleId = ur.RoleId,
                        roleName = ur.Role.RoleName,
                        isActive = ur.Role.IsActive
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (row is null)
        {
            return NotFound(new { message = "Khong tim thay quan tri vien." });
        }

        return Ok(new
        {
            row.userId,
            row.userName,
            row.fullName,
            row.email,
            row.phone,
            row.avatar,
            roleId = row.role?.roleId ?? 0,
            role = row.role,
            row.isActive,
            row.lastLogin,
            row.created,
            row.updated
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserRequest request)
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

        var duplicateUserName = await _db.Users.AnyAsync(u => u.UserName == normalized.userName);
        if (duplicateUserName)
        {
            return Conflict(new { message = "Ten dang nhap da ton tai." });
        }

        var duplicateEmail = await _db.Users.AnyAsync(u => u.Email == normalized.email);
        if (duplicateEmail)
        {
            return Conflict(new { message = "Email da duoc su dung." });
        }

        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            var duplicatePhone = await _db.Users.AnyAsync(u => u.Phone == normalized.phone);
            if (duplicatePhone)
            {
                return Conflict(new { message = "So dien thoai da duoc su dung." });
            }
        }

        Role? selectedRole = null;
        if (normalized.roleId > 0)
        {
            selectedRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleId == normalized.roleId && r.IsActive && r.RoleName != "Customer");
            if (selectedRole is null)
            {
                return BadRequest(new { message = "Role khong hop le." });
            }
        }

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            var user = new User
            {
                UserName = normalized.userName,
                FullName = normalized.fullName,
                Email = normalized.email,
                Phone = normalized.phone,
                Avatar = normalized.avatar,
                IsActive = normalized.isActive,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            _db.UserAuths.Add(new UserAuth
            {
                UserId = user.UserId,
                PasswordHash = _passwordHasher.HashPassword(user, normalized.password),
                FailedCount = 0,
                UpdatedAt = now
            });

            if (selectedRole is not null)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = selectedRole.RoleId,
                    CreatedAt = now
                });
            }

            await _db.SaveChangesAsync();
            await tran.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Them quan tri vien thanh cong.",
                userId = user.UserId
            });
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync();
            return BadRequest(new { message = "Khong the them quan tri vien.", detail = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] AdminUpdateUserRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Du lieu khong hop le." });
        }

        var user = await _db.Users
            .Include(u => u.UserAuth)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == id && u.UserRoles.Any(ur => ur.Role.RoleName != "Customer"));

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay quan tri vien." });
        }

        var normalized = NormalizeUpdateRequest(request);
        if (!normalized.isValid)
        {
            return BadRequest(new { message = normalized.error });
        }

        var duplicateUserName = await _db.Users.AnyAsync(u => u.UserId != id && u.UserName == normalized.userName);
        if (duplicateUserName)
        {
            return Conflict(new { message = "Ten dang nhap da ton tai." });
        }

        var duplicateEmail = await _db.Users.AnyAsync(u => u.UserId != id && u.Email == normalized.email);
        if (duplicateEmail)
        {
            return Conflict(new { message = "Email da duoc su dung." });
        }

        if (!string.IsNullOrWhiteSpace(normalized.phone))
        {
            var duplicatePhone = await _db.Users.AnyAsync(u => u.UserId != id && u.Phone == normalized.phone);
            if (duplicatePhone)
            {
                return Conflict(new { message = "So dien thoai da duoc su dung." });
            }
        }

        Role? selectedRole = null;
        if (normalized.roleId > 0)
        {
            selectedRole = await _db.Roles.FirstOrDefaultAsync(r => r.RoleId == normalized.roleId && r.IsActive && r.RoleName != "Customer");
            if (selectedRole is null)
            {
                return BadRequest(new { message = "Role khong hop le." });
            }
        }

        var now = DateTime.UtcNow;

        user.UserName = normalized.userName;
        user.FullName = normalized.fullName;
        user.Email = normalized.email;
        user.Phone = normalized.phone;
        user.Avatar = normalized.avatar;
        user.IsActive = normalized.isActive;
        user.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(normalized.newPassword))
        {
            if (user.UserAuth is null)
            {
                user.UserAuth = new UserAuth
                {
                    UserId = user.UserId,
                    FailedCount = 0,
                    UpdatedAt = now
                };
            }

            user.UserAuth.PasswordHash = _passwordHasher.HashPassword(user, normalized.newPassword);
            user.UserAuth.UpdatedAt = now;
        }

        var currentAdminRoles = user.UserRoles.Where(ur => ur.Role.RoleName != "Customer").ToList();
        if (currentAdminRoles.Count > 0)
        {
            _db.UserRoles.RemoveRange(currentAdminRoles);
        }

        if (selectedRole is not null)
        {
            _db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId,
                RoleId = selectedRole.RoleId,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Cap nhat quan tri vien thanh cong." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        if (TryGetCurrentUserId(out var currentUserId) && currentUserId == id)
        {
            return BadRequest(new { message = "Khong the tu xoa tai khoan dang dang nhap." });
        }

        var user = await _db.Users
            .Include(u => u.UserAuth)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Include(u => u.UserSessions)
            .Include(u => u.AddressBook)
            .FirstOrDefaultAsync(u => u.UserId == id && u.UserRoles.Any(ur => ur.Role.RoleName != "Customer"));

        if (user is null)
        {
            return NotFound(new { message = "Khong tim thay quan tri vien." });
        }

        if (user.UserAuth is not null)
        {
            _db.UserAuths.Remove(user.UserAuth);
        }

        if (user.UserRoles.Count > 0)
        {
            _db.UserRoles.RemoveRange(user.UserRoles);
        }

        if (user.UserSessions.Count > 0)
        {
            _db.UserSessions.RemoveRange(user.UserSessions);
        }

        if (user.AddressBook is not null)
        {
            _db.AddressBooks.Remove(user.AddressBook);
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Xoa quan tri vien thanh cong." });
    }

    private static (bool isValid, string? error, string userName, string fullName, string email, string password, string? phone, string? avatar, bool isActive, int roleId)
        NormalizeCreateRequest(AdminCreateUserRequest request)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.Avatar) ? null : request.Avatar.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            return (false, "Ten dang nhap khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Ho ten khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "Mat khau khong duoc de trong.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        if (!request.RoleId.HasValue || request.RoleId.Value <= 0)
        {
            return (false, "Vui long chon role cho nguoi dung.", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true, 0);
        }

        return (true, null, userName, fullName, email, password, phone, avatar, request.IsActive ?? true, request.RoleId ?? 0);
    }

    private static (bool isValid, string? error, string userName, string fullName, string email, string? newPassword, string? phone, string? avatar, bool isActive, int roleId)
        NormalizeUpdateRequest(AdminUpdateUserRequest request)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.Avatar) ? null : request.Avatar.Trim();
        var newPassword = string.IsNullOrWhiteSpace(request.NewPassword) ? null : request.NewPassword.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            return (false, "Ten dang nhap khong duoc de trong.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return (false, "Ho ten khong duoc de trong.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return (false, "Email khong duoc de trong.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        if (!request.RoleId.HasValue || request.RoleId.Value <= 0)
        {
            return (false, "Vui long chon role cho nguoi dung.", string.Empty, string.Empty, string.Empty, null, null, null, true, 0);
        }

        return (true, null, userName, fullName, email, newPassword, phone, avatar, request.IsActive ?? true, request.RoleId ?? 0);
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;

        var claim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(claim, out userId);
    }

    public sealed class AdminCreateUserRequest
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string? Phone { get; set; }

        public string? Avatar { get; set; }

        public bool? IsActive { get; set; }

        public int? RoleId { get; set; }
    }

    public sealed class AdminUpdateUserRequest
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? NewPassword { get; set; }

        public string? Phone { get; set; }

        public string? Avatar { get; set; }

        public bool? IsActive { get; set; }

        public int? RoleId { get; set; }
    }
}
