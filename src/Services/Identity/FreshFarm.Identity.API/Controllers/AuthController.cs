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
using Microsoft.AspNetCore.Authorization; // Dùng [Authorize].
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
        [HttpGet("profile")] // GET /auth/profile trả thông tin profile của user đăng nhập.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Chỉ cho token JWT hợp lệ.
        public async Task<IActionResult> GetProfile() // Action lấy profile hiện tại.
        {
            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // Token lỗi thì trả 401.
            }

            var user = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == userId); // Đọc user hiện tại.
            if (user is null) // Không tìm thấy user.
            {
                return NotFound("Không tìm thấy tài khoản."); // Trả 404.
            }

            var dto = new ProfileResponseDto // Map entity sang DTO trả về.
            {
                UserId = user.UserId, // Map UserId.
                UserName = user.UserName, // Map UserName.
                FullName = user.FullName, // Map FullName.
                Email = user.Email, // Map Email.
                Phone = user.Phone // Map Phone.
            };

            return Ok(dto); // Trả 200 + dữ liệu profile.
        }

        [HttpPut("profile")] // PUT /auth/profile cập nhật profile.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Bắt buộc JWT.
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequestDto request) // Nhận payload update profile.
        {
            if (!ModelState.IsValid) // Chặn ngay payload sai format.
            {
                return ValidationProblem(ModelState); // Trả lỗi validate chuẩn ASP.NET Core.
            }

            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // Token sai.
            }

            var user = await _db.Users.SingleOrDefaultAsync(u => u.UserId == userId); // Lấy user cần sửa.
            if (user is null) // Nếu không tồn tại user.
            {
                return NotFound("Không tìm thấy tài khoản."); // Trả 404.
            }

            var normalizedEmail = request.Email.Trim(); // Chuẩn hóa email trước khi so/ghi.
            var normalizedPhone = request.Phone.Trim(); // Chuẩn hóa phone trước khi so/ghi.
            var normalizedFullName = request.FullName.Trim(); // Chuẩn hóa fullname.

            var emailExists = await _db.Users.AnyAsync(u => u.UserId != userId && u.Email == normalizedEmail); // Kiểm tra trùng email user khác.
            if (emailExists) // Nếu trùng email.
            {
                return Conflict("Email đã được sử dụng bởi tài khoản khác."); // Trả 409.
            }

            var phoneExists = await _db.Users.AnyAsync(u => u.UserId != userId && u.Phone == normalizedPhone); // Kiểm tra trùng phone user khác.
            if (phoneExists) // Nếu trùng phone.
            {
                return Conflict("Số điện thoại đã được sử dụng bởi tài khoản khác."); // Trả 409.
            }

            user.FullName = normalizedFullName; // Gán lại fullname mới.
            user.Email = normalizedEmail; // Gán lại email mới.
            user.Phone = normalizedPhone; // Gán lại phone mới.
            user.UpdatedAt = DateTime.UtcNow; // Ghi timestamp cập nhật.

            await _db.SaveChangesAsync(); // Lưu thay đổi vào DB.

            return Ok(new ProfileResponseDto // Trả lại profile mới nhất cho client.
            {
                UserId = user.UserId, // Trả UserId.
                UserName = user.UserName, // Trả UserName.
                FullName = user.FullName, // Trả FullName mới.
                Email = user.Email, // Trả Email mới.
                Phone = user.Phone // Trả Phone mới.
            });
        }

        [HttpGet("addresses")] // GET /auth/addresses lấy danh sách địa chỉ của user.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Bắt buộc JWT.
        public async Task<IActionResult> GetAddresses() // Action list addresses.
        {
            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // 401 nếu token sai.
            }

            var addresses = await _db.AddressBooks // Query bảng AddressBook.
                .AsNoTracking() // Không tracking để đọc nhanh.
                .Where(a => a.UserId == userId && a.IsActive) // Chỉ lấy địa chỉ active của user hiện tại.
                .OrderByDescending(a => a.IsDefault) // Cho địa chỉ mặc định lên đầu.
                .ThenByDescending(a => a.UpdatedAt ?? a.CreatedAt) // Sau đó sort theo mới nhất.
                .Select(a => new AddressResponseDto // Map sang DTO trả về.
                {
                    AddressId = a.AddressId, // Map AddressId.
                    RecipientName = a.RecipientName, // Map RecipientName.
                    Phone = a.Phone, // Map Phone.
                    AddressDetail = a.AddressDetail, // Map AddressDetail.
                    Province = a.Province, // Map Province.
                    District = a.District, // Map District.
                    Ward = a.Ward, // Map Ward.
                    IsDefault = a.IsDefault // Map IsDefault.
                })
                .ToListAsync(); // Materialize list async.

            return Ok(addresses); // Trả 200 + danh sách địa chỉ.
        }

        [HttpPost("addresses")] // POST /auth/addresses tạo địa chỉ mới.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Bắt buộc JWT.
        public async Task<IActionResult> CreateAddress([FromBody] UpsertAddressRequestDto request) // Nhận payload tạo địa chỉ.
        {
            if (!ModelState.IsValid) // Validate payload.
            {
                return ValidationProblem(ModelState); // Trả lỗi validate.
            }

            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // 401 nếu sai.
            }

            var now = DateTime.UtcNow; // Timestamp dùng chung cho bản ghi mới.

            if (request.IsDefault) // Nếu địa chỉ mới được set mặc định.
            {
                var oldDefaults = await _db.AddressBooks // Lấy các địa chỉ mặc định cũ.
                    .Where(a => a.UserId == userId && a.IsActive && a.IsDefault) // Đúng user, active, mặc định.
                    .ToListAsync(); // Query list.

                foreach (var item in oldDefaults) // Duyệt từng địa chỉ mặc định cũ.
                {
                    item.IsDefault = false; // Tắt cờ mặc định cũ.
                    item.UpdatedAt = now; // Ghi timestamp cập nhật.
                }
            }

            var entity = new AddressBook // Tạo entity địa chỉ mới.
            {
                UserId = userId, // Gán user hiện tại.
                RecipientName = request.RecipientName.Trim(), // Gán và trim RecipientName.
                Phone = request.Phone.Trim(), // Gán và trim Phone.
                AddressDetail = request.AddressDetail.Trim(), // Gán và trim AddressDetail.
                Province = string.IsNullOrWhiteSpace(request.Province) ? null : request.Province.Trim(), // Gán Province nếu có.
                District = string.IsNullOrWhiteSpace(request.District) ? null : request.District.Trim(), // Gán District nếu có.
                Ward = string.IsNullOrWhiteSpace(request.Ward) ? null : request.Ward.Trim(), // Gán Ward nếu có.
                IsDefault = request.IsDefault, // Gán cờ mặc định theo request.
                IsActive = true, // Mặc định active.
                CreatedAt = now, // Ngày tạo.
                UpdatedAt = now // Ngày cập nhật ban đầu.
            };

            _db.AddressBooks.Add(entity); // Add entity vào DbContext.
            await _db.SaveChangesAsync(); // Lưu xuống DB.

            return Ok(new AddressResponseDto // Trả lại địa chỉ vừa tạo.
            {
                AddressId = entity.AddressId, // Trả AddressId vừa sinh.
                RecipientName = entity.RecipientName, // Trả RecipientName.
                Phone = entity.Phone, // Trả Phone.
                AddressDetail = entity.AddressDetail, // Trả AddressDetail.
                Province = entity.Province, // Trả Province.
                District = entity.District, // Trả District.
                Ward = entity.Ward, // Trả Ward.
                IsDefault = entity.IsDefault // Trả IsDefault.
            });
        }

        [HttpPut("addresses/{addressId:int}")] // PUT /auth/addresses/{id} cập nhật địa chỉ.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Bắt buộc JWT.
        public async Task<IActionResult> UpdateAddress(int addressId, [FromBody] UpsertAddressRequestDto request) // Nhận id + payload update.
        {
            if (!ModelState.IsValid) // Validate payload.
            {
                return ValidationProblem(ModelState); // Trả lỗi validation.
            }

            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // 401.
            }

            var entity = await _db.AddressBooks // Query địa chỉ cần sửa.
                .SingleOrDefaultAsync(a => a.AddressId == addressId && a.UserId == userId && a.IsActive); // Chỉ cho sửa địa chỉ của chính user.
            if (entity is null) // Không tìm thấy.
            {
                return NotFound("Không tìm thấy địa chỉ cần cập nhật."); // Trả 404.
            }

            var now = DateTime.UtcNow; // Timestamp update.

            if (request.IsDefault) // Nếu request muốn set địa chỉ này thành mặc định.
            {
                var oldDefaults = await _db.AddressBooks // Lấy default cũ của user.
                    .Where(a => a.UserId == userId && a.IsActive && a.IsDefault && a.AddressId != addressId) // Trừ chính địa chỉ đang sửa.
                    .ToListAsync(); // Query list.

                foreach (var item in oldDefaults) // Duyệt list default cũ.
                {
                    item.IsDefault = false; // Tắt default cũ.
                    item.UpdatedAt = now; // Cập nhật timestamp.
                }
            }

            entity.RecipientName = request.RecipientName.Trim(); // Cập nhật RecipientName.
            entity.Phone = request.Phone.Trim(); // Cập nhật Phone.
            entity.AddressDetail = request.AddressDetail.Trim(); // Cập nhật AddressDetail.
            entity.Province = string.IsNullOrWhiteSpace(request.Province) ? null : request.Province.Trim(); // Cập nhật Province.
            entity.District = string.IsNullOrWhiteSpace(request.District) ? null : request.District.Trim(); // Cập nhật District.
            entity.Ward = string.IsNullOrWhiteSpace(request.Ward) ? null : request.Ward.Trim(); // Cập nhật Ward.
            entity.IsDefault = request.IsDefault; // Cập nhật cờ default.
            entity.UpdatedAt = now; // Ghi timestamp.

            await _db.SaveChangesAsync(); // Lưu DB.

            return Ok(new AddressResponseDto // Trả bản ghi sau cập nhật.
            {
                AddressId = entity.AddressId, // Trả AddressId.
                RecipientName = entity.RecipientName, // Trả RecipientName.
                Phone = entity.Phone, // Trả Phone.
                AddressDetail = entity.AddressDetail, // Trả AddressDetail.
                Province = entity.Province, // Trả Province.
                District = entity.District, // Trả District.
                Ward = entity.Ward, // Trả Ward.
                IsDefault = entity.IsDefault // Trả IsDefault.
            });
        }

        [HttpPost("addresses/{addressId:int}/set-default")] // POST /auth/addresses/{id}/set-default đặt mặc định.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Bắt buộc JWT.
        public async Task<IActionResult> SetDefaultAddress(int addressId) // Nhận addressId cần set mặc định.
        {
            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // 401.
            }

            var entity = await _db.AddressBooks // Tìm địa chỉ mục tiêu.
                .SingleOrDefaultAsync(a => a.AddressId == addressId && a.UserId == userId && a.IsActive); // Bắt buộc thuộc user.
            if (entity is null) // Không thấy.
            {
                return NotFound("Không tìm thấy địa chỉ cần đặt mặc định."); // 404.
            }

            var now = DateTime.UtcNow; // Timestamp chung.

            var oldDefaults = await _db.AddressBooks // Lấy địa chỉ mặc định cũ.
                .Where(a => a.UserId == userId && a.IsActive && a.IsDefault && a.AddressId != addressId) // Trừ địa chỉ mới.
                .ToListAsync(); // Query list.

            foreach (var item in oldDefaults) // Duyệt default cũ.
            {
                item.IsDefault = false; // Tắt default cũ.
                item.UpdatedAt = now; // Cập nhật timestamp.
            }

            entity.IsDefault = true; // Set default mới.
            entity.UpdatedAt = now; // Cập nhật timestamp.

            await _db.SaveChangesAsync(); // Lưu DB.
            return NoContent(); // Trả 204 theo semantics action set-default.
        }

        [HttpDelete("addresses/{addressId:int}")] // DELETE /auth/addresses/{id} xóa mềm địa chỉ.
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // Bắt buộc JWT.
        public async Task<IActionResult> DeleteAddress(int addressId) // Nhận id địa chỉ cần xóa.
        {
            if (!TryGetCurrentUserId(out var userId)) // Lấy userId an toàn từ JWT claims.
            {
                return Unauthorized("Token không chứa user id hợp lệ."); // 401.
            }

            var entity = await _db.AddressBooks // Query địa chỉ cần xóa.
                .SingleOrDefaultAsync(a => a.AddressId == addressId && a.UserId == userId && a.IsActive); // Chỉ xóa địa chỉ active của user.
            if (entity is null) // Không tìm thấy.
            {
                return NotFound("Không tìm thấy địa chỉ cần xóa."); // 404.
            }

            entity.IsActive = false; // Soft delete để giữ lịch sử.
            entity.IsDefault = false; // Bỏ cờ mặc định nếu có.
            entity.UpdatedAt = DateTime.UtcNow; // Ghi timestamp.

            await _db.SaveChangesAsync(); // Lưu DB.
            return NoContent(); // Trả 204 khi xóa thành công.
        }

        private bool TryGetCurrentUserId(out int userId) // Lấy user id từ claim đã map hoặc claim gốc.
        {
            userId = 0; // Giá trị mặc định nếu parse thất bại.

            var rawUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) // Claim gốc "sub" (khi không map).
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier) // Claim đã map mặc định của JwtBearer.
                ?? User.FindFirstValue("sub"); // Fallback cứng.

            return int.TryParse(rawUserId, out userId); // Parse an toàn sang int.
        }
    }
    
}
