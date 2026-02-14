using FreshFarm.Identity.Api.Models;
using FreshFarm.Identity.Api.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace FreshFarm.Identity.Api.Controllers
{

    [Route("auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly FreshFarmIdentityDBContext _db;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _config;

        public AuthController(FreshFarmIdentityDBContext db, IPasswordHasher<User> passwordHasher, IConfiguration config)
        {
            _db = db;
            _passwordHasher = passwordHasher;
            _config = config;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Vui lòng nhập đầy đủ Email/Username và Mật khẩu");
            }
            var id = request.Identifier.Trim();
            var isEmail = id.Contains("@");
            var user = await _db.Users
                .Include(u=>u.UserAuth)
                .Include(u=>u.UserRoles)
                .ThenInclude(ur=>ur.Role)
                .SingleOrDefaultAsync(u => isEmail ? u.Email == id : u.UserName == id);
            if(user?.UserAuth == null)
            {
                return Unauthorized("Tài khoản hoặc mật khẩu không đúng.");
            }
           
            if (!user.IsActive)
            {
                return Unauthorized("Tài khoản của bạn đã bị vô hiệu hóa.");
            }
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.UserAuth.PasswordHash, request.Password);
            if(verificationResult == PasswordVerificationResult.Failed)
            {
                 return Unauthorized("Tài khoản hoặc mật khẩu không đúng.");
            }
            var roleNames = user.UserRoles.Select(ur => ur.Role.RoleName).ToList();
            var token = CreateToken(user, roleNames);
            return Ok(token);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.FullName) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Phone) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return BadRequest("Toàn bộ thông tin là bắt buộc nhập.");
            }
            var existingUser = await _db.Users.AnyAsync(u=>u.Email == request.Email.Trim() || u.UserName == request.UserName.Trim() || u.Phone == request.Phone.Trim());
            if(existingUser) 
            {
                return Conflict("Email/Phone/UserName aleady exists.");
            }
            if(request.Password != request.ConfirmPassword)
            {
                return BadRequest("Mật khẩu xác nhận không khớp.");
            }

            var roleName = string.IsNullOrEmpty(request.RoleName) ? "Customer" : request.RoleName.Trim();
            var role = await _db.Roles.SingleOrDefaultAsync(r => r.RoleName == roleName);
            if (role == null)
            {
                return BadRequest($"Vai trò {roleName} không tìm thấy.");
            }
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var user = new User
                {
                    Email = request.Email.Trim(),
                    UserName = request.UserName.Trim(),
                    FullName = request.FullName.Trim(),
                    Phone = request.Phone.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync();
                var hashedPassword = _passwordHasher.HashPassword(user, request.Password);
                _db.UserAuths.Add(new UserAuth
                {
                    UserId = user.UserId,
                    PasswordHash = hashedPassword,
                    FailedCount = 0,
                    UpdatedAt = DateTime.UtcNow
                });



                _db.UserRoles.Add(new UserRole
                {
                    RoleId = role.RoleId,
                    UserId = user.UserId,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new { user.UserId, user.UserName, user.Email, role.RoleName });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, "Có lỗi xảy ra khi đăng kí, vui lòng thử lại");
            }
           
        }
        private AuthResponse CreateToken(User user, IEnumerable<string> roles)
        {
            var jwtKey = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException("JWT Key chưa được cấu hình.");
            }
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),

                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim("username", user.UserName ?? "")

            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var expires = DateTime.UtcNow.AddHours(1);
            var jwt = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],

                claims: claims,
                expires: expires,
                signingCredentials: creds
            );
            return new AuthResponse
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(jwt),
                ExpiredAtUtc = expires
            };
        }
    }
    
}
