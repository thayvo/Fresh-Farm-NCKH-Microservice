# FreshFarm — Sàn thương mại điện tử nông sản trên kiến trúc microservices

FreshFarm là đề tài nghiên cứu khoa học xây dựng sàn TMĐT hỗ trợ tiêu thụ nông sản: khách hàng mua hàng, người bán (seller) trưng bày và quản lý kho, admin quản trị toàn hệ thống. Toàn hệ thống được dựng trên **.NET 8 microservices** với trọng tâm nghiên cứu là **bảo mật xác thực** và **hệ thống gợi ý sản phẩm lai (hybrid recommendation)**.

## Kiến trúc

```
                    ┌──────────────────────────────┐
   Trình duyệt ───► │  FreshFarm.Web.Bff (MVC)     │ :7085
                    │  Customer / Seller / Admin   │──┐
                    └──────────────────────────────┘  │ HTTPS + JWT + Internal Key
        ┌──────────────────┬─────────────────────────┼──────────────┐
        ▼                  ▼                         ▼              │
┌───────────────┐  ┌───────────────┐  ┌─────────────────────┐      │
│ Identity API  │  │ Catalog API   │  │ Ordering API        │◄─────┘
│ :7140         │  │ :7245         │  │ :7018               │
│ Đăng nhập,    │  │ Sản phẩm,     │  │ Giỏ hàng, đơn hàng, │
│ JWT, duyệt    │  │ offer, kho,   │  │ VNPay/GHN, đánh giá,│
│ tài khoản     │  │ theo mùa vụ   │  │ gợi ý (ML.NET)      │
└───────┬───────┘  └───────┬───────┘  └──────────┬──────────┘
        ▼                  ▼                     ▼
  FreshFarmIdentityDB  FreshFarmCatalogDB  FreshFarmOrderingDB   (SQL Server, DB-per-service)

  Redis (BFF): phiên, tín hiệu gợi ý theo session — tự fallback in-memory nếu không có Redis
```

| Service | Port HTTPS | Vai trò | Database |
|---|---|---|---|
| `FreshFarm.Identity.API` | 7140 | Đăng ký/đăng nhập, JWT, xác minh email, Google OAuth, 2FA, duyệt tài khoản, khóa thiết bị | `FreshFarmIdentityDB` |
| `FreshFarm.Catalog.Api` | 7245 | Sản phẩm, offer, danh mục, kho/lô hàng, thu hồi, tính mùa vụ, đặt giữ tồn kho nội bộ | `FreshFarmCatalogDB` |
| `FreshFarm.Ordering.Api` | 7018 | Giỏ hàng, đơn hàng, VNPay, tích hợp GHN, đánh giá, coupon, tài chính, chat, pipeline gợi ý | `FreshFarmOrderingDB` |
| `FreshFarm.Web.Bff` | 7085 | MVC Backend-for-Frontend cho Customer/Seller/Admin, CAPTCHA, webhook GHN, rerank gợi ý | — (cookie phiên + Redis) |

**Giao tiếp giữa các service:** JWT bearer dùng chung (Issuer `FreshFarm.Identity`), kèm internal key riêng từng tuyến (`X-FreshFarm-Internal-Key` cho BFF→Identity, `X-Service-Key` cho Ordering→Catalog). Catalog/Ordering xác thực phiên sống với Identity **mỗi request** (`IdentitySessionValidation`, fail-closed).

## Công nghệ

- .NET 8 (ASP.NET Core MVC / Web API), EF Core + SQL Server (DB-per-service, 20 migrations)
- ASP.NET Core Identity, JWT, cookie authentication, Google OAuth, TOTP 2FA
- Cloudflare Turnstile / Google reCAPTCHA / CAPTCHA ảnh (Development)
- ML.NET `Microsoft.ML.Recommender` (Matrix Factorization), Redis (StackExchange.Redis)
- SignalR (support chat), xUnit (4 test project, 405 test)

## Cấu trúc repo

```
src/
  FreshFarm/FreshFarm.sln          # solution chung
  BuildingBlocks/FreshFarm.Contracts
  Services/
    Identity/FreshFarm.Identity.API
    Catalog/FreshFarm.Catalog.Api
    Ordering/FreshFarm.Ordering.Api
  Web/FreshFarm.Web.Bff             # giao diện người dùng
  Tests/                            # 4 test project xUnit
scripts/                            # start/stop/restart local, smoke test
.github/workflows/dotnet-build.yml  # CI build
```

`src/README.md` là nhật ký hướng dẫn tự dựng ban đầu của dự án, giữ làm tài liệu tham khảo lịch sử (một số port/cấu trúc đã thay đổi — xem bảng trên là chuẩn hiện tại).

## Yêu cầu môi trường

- .NET 8 SDK
- SQL Server (LocalDB hoặc full) — 3 database riêng cho 3 service
- Redis (tuỳ chọn cho BFF — không có sẽ tự fallback in-memory)
- `dotnet tool install --global dotnet-ef` (chạy migration)

## Cấu hình

Mỗi service có file mẫu `appsettings.Development.json.example`. Cách nhanh:

```bash
# ví dụ cho Catalog — làm tương tự với Identity, Ordering, BFF
cp src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.Development.json.example \
   src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.Development.json
```

Các giá trị **bắt buộc phải điền** (không commit lên Git — dùng `dotnet user-secrets` hoặc điền vào `appsettings.Development.json` ở máy cá nhân):

| Khóa | Service | Ý nghĩa |
|---|---|---|
| `ConnectionStrings:IdentityDB` / `FreshFarmCatalogDB` / `FreshFarmOrderingDB` | Identity / Catalog / Ordering | Chuỗi kết nối SQL Server |
| `Jwt:Key` | cả 3 API | Khóa ký JWT, **tối thiểu 32 ký tự, giống nhau ở cả 3 API** (Issuer `FreshFarm.Identity`, Audience `FreshFarm`) |
| `InternalBff:SharedKey` (Identity) = `Services:Identity:InternalServiceKey` (BFF) | Identity + BFF | Internal key BFF gọi endpoint nhạy cảm của Identity |
| `Services:Internal:ServiceKey` (Catalog) = khóa tương ứng phía Ordering | Catalog + Ordering | Internal key đặt giữ/commit tồn kho |
| `Smtp:*`, `EmailVerification:VerifyUrlBase`, `PasswordReset:ResetUrlBase` | Identity | Gửi email xác minh / đặt lại mật khẩu |
| `Authentication:Google:ClientId/ClientSecret` | BFF | Google OAuth đăng nhập |
| `Security:CloudflareTurnstile:*` hoặc `Security:GoogleRecaptcha:*` | BFF | CAPTCHA production (phải khai báo `AllowedHostnames`) |
| `DataProtection:CertificateThumbprint` | Identity + BFF | Certificate bảo vệ key — **Production fail-closed nếu thiếu** |

Webhook GHN dùng header `X-Webhook-Secret` (khuyến nghị reverse proxy tin cậy chèn); VNPay/MoMo/GHN credentials nạp qua user-secrets/env khi deploy.

## Tạo database & chạy migration

```bash
dotnet ef database update --project src/Services/Identity/FreshFarm.Identity.API
dotnet ef database update --project src/Services/Catalog/FreshFarm.Catalog.Api
dotnet ef database update --project src/Services/Ordering/FreshFarm.Ordering.Api
```

Mỗi service có design-time factory riêng nên không cần thêm tham số. Catalog/Ordering cũng có script SQL idempotent trong tài liệu dự án nếu cần nạp tay.

## Chạy local

Cách khuyến nghị — build Release từ source rồi khởi động đủ 4 service, tự poll `/health`:

```powershell
powershell -File scripts/start-local-core.ps1          # build + chạy cả 4
powershell -File scripts/start-local-core.ps1 -SkipBuild  # bỏ build
powershell -File scripts/stop-local-core.ps1
powershell -File scripts/restart-local-core.ps1
```

Mở app tại **https://localhost:7085**. Kiểm tra sức khỏe từng service:

- https://localhost:7140/health — Identity
- https://localhost:7245/health — Catalog
- https://localhost:7018/health — Ordering
- https://localhost:7085/health và `/health/redis` — BFF

## Luồng tài khoản

Đăng ký công khai luôn tạo tài khoản **Customer, trạng thái Pending**. Tài khoản Pending đã xác minh email đăng nhập được với quyền **Khách** (chỉ xem trạng thái), mọi chức năng nghiệp vụ chỉ mở sau khi **Admin duyệt** (`account_access=full`). Đăng nhập Google yêu cầu `email_verified=true`. Sai mật khẩu 5 lần sẽ khóa tạm tài khoản + thiết bị (15 phút, tăng dần tới 24 giờ; admin có thể mở khóa).

## Chạy test

```bash
dotnet test src/Tests/FreshFarm.Identity.Api.Tests
dotnet test src/Tests/FreshFarm.Catalog.Api.Tests
dotnet test src/Tests/FreshFarm.Ordering.Api.Tests
dotnet test src/Tests/FreshFarm.Web.Bff.Tests
```

405 test phủ: khóa thiết bị/tài khoản kèm harness chạy 20 request đồng thời, duyệt tài khoản, thu hồi phiên sống, fail-closed internal key, idempotency webhook GHN, rerank/thí nghiệm gợi ý, v.v.

## Bảo mật (tóm tắt)

- Default/fallback authorization **fail-closed**: mọi endpoint mặc định yêu cầu đăng nhập + `account_access=full`; Pending chỉ xem trạng thái.
- Thu hồi phiên **ngay lập tức**: đổi mật khẩu/duyệt từ chối tăng `TokenVersion`, Catalog/Ordering xác minh trực tiếp với Identity mỗi request.
- Internal endpoint đặt sau internal key so sánh constant-time; không thể vào bằng JWT hợp lệ.
- Rate limiting 11 policy (đăng nhập, phục hồi mật khẩu, checkout, review...), CAPTCHA xác minh server-side kèm kiểm tra action + hostname allowlist.
- KYC seller lưu ngoài webroot, tải qua endpoint có kiểm tra quyền sở hữu. Data Protection production bắt buộc certificate.

## License

Dùng cho mục đích nghiên cứu khoa học và học tập.
