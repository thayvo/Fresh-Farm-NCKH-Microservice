# `src/` – Hướng dẫn tự dựng microservices (từng bước)

Mục tiêu: bạn tự tay dựng kiến trúc microservices trong **cùng repo** (mono-repo), còn project `NCKH-FRESH-FARM/` hiện tại giữ làm “legacy/monolith tham khảo”.

## Tiến độ (tracker)

- [x] Tạo `src/` + cấu trúc `BuildingBlocks/Services/Web`
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Tạo solution microservices `src/FreshFarm/FreshFarm.sln`
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Tạo `FreshFarm.Contracts` (class library)
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Tạo `FreshFarm.Identity.Api` (Web API) + add vào solution
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Tạo `FreshFarm.Catalog.Api` (Web API) + add vào solution
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Tạo `FreshFarm.Web.Bff` (MVC) + add vào solution
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Add `ProjectReference` tới `FreshFarm.Contracts` cho Identity/Catalog/Ordering/BFF
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Set port cố định cho 4 app (launchSettings hoặc env vars)
  - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Thêm endpoint `/health` cho Identity/Catalog/Ordering/BFF
- [x] Implement login/register + JWT ở Identity
  - [x] Thêm `Jwt` section vào `Identity/appsettings.json`
    - ---------=====================ĐÃ HOÀN THÀNH==========-------
- [x] Protect Catalog bằng JWT role/claims (Seller)
- [x] BFF gọi Identity/Catalog/Ordering bằng typed `HttpClient` và có trang login (Admin/Seller/Customer)
- [ ] Dockerfile + `src/docker-compose.yml` để chạy 4 app (tuỳ chọn)

## 0) Yêu cầu môi trường

- .NET SDK 8 (`dotnet --version`)
- (Tuỳ chọn) SQL Server local nếu bạn muốn làm DB thật ngay

## 1) Chuẩn cấu trúc thư mục (đã có)

```
src/
  BuildingBlocks/
  Services/
  Web/
```

## 1.1) Cấu trúc repo hiện tại (snapshot)

Bỏ qua các thư mục build/cache như `.vs/`, `bin/`, `obj/`.

<!-- STRUCTURE_SNAPSHOT_START -->
```text
repo-root/
  docs/
  docs/marketplace-db-review.md
  docs/marketplace-idea.md
  docs/marketplace-schema-samples.md
  NCKH-FRESH-FARM/
  NCKH-FRESH-FARM/Areas/
  NCKH-FRESH-FARM/Areas/Admin/
  NCKH-FRESH-FARM/Areas/Admin/Controllers/
  NCKH-FRESH-FARM/Areas/Admin/Models/
  NCKH-FRESH-FARM/Areas/Admin/Views/
  NCKH-FRESH-FARM/Areas/Admin/Views/Shared/
  NCKH-FRESH-FARM/Areas/Seller/
  NCKH-FRESH-FARM/Areas/Seller/Controllers/
  NCKH-FRESH-FARM/Areas/Seller/Models/
  NCKH-FRESH-FARM/Areas/Seller/Views/
  NCKH-FRESH-FARM/Areas/Seller/Views/Shared/
  NCKH-FRESH-FARM/Controllers/
  NCKH-FRESH-FARM/Data/
  NCKH-FRESH-FARM/Domain/
  NCKH-FRESH-FARM/Domain/Users/
  NCKH-FRESH-FARM/Models/
  NCKH-FRESH-FARM/Properties/
  NCKH-FRESH-FARM/Services/
  NCKH-FRESH-FARM/Services/Implementations/
  NCKH-FRESH-FARM/Services/Interfaces/
  NCKH-FRESH-FARM/Views/
  NCKH-FRESH-FARM/Views/Home/
  NCKH-FRESH-FARM/Views/Shared/
  NCKH-FRESH-FARM/wwwroot/
  NCKH-FRESH-FARM/wwwroot/css/
  NCKH-FRESH-FARM/wwwroot/js/
  NCKH-FRESH-FARM/wwwroot/lib/
  NCKH-FRESH-FARM/wwwroot/lib/bootstrap/
  NCKH-FRESH-FARM/wwwroot/lib/bootstrap/dist/
  NCKH-FRESH-FARM/wwwroot/lib/bootstrap/dist/css/
  NCKH-FRESH-FARM/wwwroot/lib/bootstrap/dist/js/
  NCKH-FRESH-FARM/wwwroot/lib/jquery/
  NCKH-FRESH-FARM/wwwroot/lib/jquery/dist/
  NCKH-FRESH-FARM/wwwroot/lib/jquery-validation/
  NCKH-FRESH-FARM/wwwroot/lib/jquery-validation/dist/
  NCKH-FRESH-FARM/wwwroot/lib/jquery-validation/LICENSE.md
  NCKH-FRESH-FARM/wwwroot/lib/jquery-validation-unobtrusive/
  NCKH-FRESH-FARM/appsettings.Development.json
  NCKH-FRESH-FARM/appsettings.json
  NCKH-FRESH-FARM/ARCHITECTURE_GUIDE.md
  NCKH-FRESH-FARM/NCKH-FRESH-FARM.csproj
  NCKH-FRESH-FARM/Program.cs
  src/
  src/BuildingBlocks/
  src/BuildingBlocks/FreshFarm.Contracts/
  src/BuildingBlocks/FreshFarm.Contracts/FreshFarm.Contracts.csproj
  src/FreshFarm/
  src/FreshFarm/FreshFarm.sln
  src/Services/
  src/Services/Catalog/
  src/Services/Catalog/FreshFarm.Catalog.Api/
  src/Services/Catalog/FreshFarm.Catalog.Api/Controllers/
  src/Services/Catalog/FreshFarm.Catalog.Api/Properties/
  src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.Development.json
  src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.json
  src/Services/Catalog/FreshFarm.Catalog.Api/FreshFarm.Catalog.Api.csproj
  src/Services/Catalog/FreshFarm.Catalog.Api/Program.cs
  src/Services/Identity/
  src/Services/Identity/FreshFarm.Identity.API/
  src/Services/Identity/FreshFarm.Identity.API/Controllers/
  src/Services/Identity/FreshFarm.Identity.API/Properties/
  src/Services/Identity/FreshFarm.Identity.API/appsettings.Development.json
  src/Services/Identity/FreshFarm.Identity.API/appsettings.json
  src/Services/Identity/FreshFarm.Identity.API/FreshFarm.Identity.Api.csproj
  src/Services/Identity/FreshFarm.Identity.API/Program.cs
  src/Web/
  src/Web/FreshFarm.Web.Bff/
  src/Web/FreshFarm.Web.Bff/Controllers/
  src/Web/FreshFarm.Web.Bff/Models/
  src/Web/FreshFarm.Web.Bff/Properties/
  src/Web/FreshFarm.Web.Bff/Views/
  src/Web/FreshFarm.Web.Bff/Views/Home/
  src/Web/FreshFarm.Web.Bff/Views/Shared/
  src/Web/FreshFarm.Web.Bff/wwwroot/
  src/Web/FreshFarm.Web.Bff/wwwroot/css/
  src/Web/FreshFarm.Web.Bff/wwwroot/js/
  src/Web/FreshFarm.Web.Bff/wwwroot/lib/
  src/Web/FreshFarm.Web.Bff/wwwroot/lib/bootstrap/
  src/Web/FreshFarm.Web.Bff/wwwroot/lib/jquery/
  src/Web/FreshFarm.Web.Bff/wwwroot/lib/jquery-validation/
  src/Web/FreshFarm.Web.Bff/wwwroot/lib/jquery-validation-unobtrusive/
  src/Web/FreshFarm.Web.Bff/appsettings.Development.json
  src/Web/FreshFarm.Web.Bff/appsettings.json
  src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj
  src/Web/FreshFarm.Web.Bff/Program.cs
  src/README.md
  NCKH-FRESH-FARM.sln
```
<!-- STRUCTURE_SNAPSHOT_END -->

## 2) Tạo solution mới (không ảnh hưởng project cũ)

Chạy ở repo root:

1. `dotnet new sln -n FreshFarm -o src`

Kỳ vọng tạo ra: `src/FreshFarm.sln`

Lưu ý: nếu bạn tạo bằng Visual Studio (Blank Solution), thường VS sẽ tạo file ở `src/FreshFarm/FreshFarm.sln`.

Nếu bạn dùng các lệnh `dotnet sln ...` bên dưới, hãy thay `src/FreshFarm.sln` bằng `src/FreshFarm/FreshFarm.sln` cho đúng với máy bạn.

## 3) Tạo `FreshFarm.Contracts` (DTO/contract dùng chung)

Mục tiêu: dùng chung DTO/IDs/enums “portable” giữa services (không chứa EF entity).

1. `dotnet new classlib -n FreshFarm.Contracts -o src/BuildingBlocks/FreshFarm.Contracts`
2. `dotnet sln src/FreshFarm.sln add src/BuildingBlocks/FreshFarm.Contracts/FreshFarm.Contracts.csproj`

Gợi ý bạn nên tạo trong `FreshFarm.Contracts`:
- `Ids/UserId.cs`, `Ids/SellerId.cs` (hoặc dùng `Guid` trực tiếp)
- `Auth/AppRole.cs` (enum Admin/Seller/Customer) *nếu thật sự cần dùng chung*
- Request/Response DTO (LoginRequest, LoginResponse, ProductDto, ListingDto…)

## 4) Tạo 3 microservice đầu tiên (Identity + Catalog + Ordering)

### 4.1) Identity service (JWT)

1. `dotnet new webapi -n FreshFarm.Identity.Api -o src/Services/Identity/FreshFarm.Identity.API`
2. `dotnet sln src/FreshFarm.sln add src/Services/Identity/FreshFarm.Identity.API/FreshFarm.Identity.Api.csproj`
3. `dotnet add src/Services/Identity/FreshFarm.Identity.API/FreshFarm.Identity.Api.csproj reference src/BuildingBlocks/FreshFarm.Contracts/FreshFarm.Contracts.csproj`

Việc bạn làm tiếp trong Identity (tự code):
- `POST /auth/register` (tạo user + role)
- `POST /auth/login` (trả JWT có claims/role)
- `GET /health` (để chứng minh service chạy độc lập)

Gợi ý làm nhanh cho portfolio:
- Giai đoạn 1: lưu user in-memory (List/Dictionary) để demo end-to-end nhanh
- Giai đoạn 2: gắn EF Core + SQL Server và tách DB `IdentityDb`

### 4.2) Catalog service

1. `dotnet new webapi -n FreshFarm.Catalog.Api -o src/Services/Catalog/FreshFarm.Catalog.Api`
2. `dotnet sln src/FreshFarm.sln add src/Services/Catalog/FreshFarm.Catalog.Api/FreshFarm.Catalog.Api.csproj`
3. `dotnet add src/Services/Catalog/FreshFarm.Catalog.Api/FreshFarm.Catalog.Api.csproj reference src/BuildingBlocks/FreshFarm.Contracts/FreshFarm.Contracts.csproj`

Việc bạn làm tiếp trong Catalog (tự code):
- `GET /products`
- `POST /seller/listings` (yêu cầu role Seller)
- `GET /seller/listings` (seller xem listing của mình)
- `GET /health`

### 4.3) Ordering service

1. `dotnet new webapi -n FreshFarm.Ordering.Api -o src/Services/Ordering/FreshFarm.Ordering.Api`
2. `dotnet sln src/FreshFarm.sln add src/Services/Ordering/FreshFarm.Ordering.Api/FreshFarm.Ordering.Api.csproj`
3. `dotnet add src/Services/Ordering/FreshFarm.Ordering.Api/FreshFarm.Ordering.Api.csproj reference src/BuildingBlocks/FreshFarm.Contracts/FreshFarm.Contracts.csproj`

Việc bạn làm tiếp trong Ordering (tự code):
- `GET /api/orders/my`
- `GET /api/orders/{id}`
- Nhóm admin route `api/orders/admin/*` cho Seller portal
- `GET /health`

## 5) Tạo Web/BFF để show UI (MVC + Areas Admin/Seller)

Mục tiêu: nhà tuyển dụng nhìn thấy “portal” rõ ràng, nhưng domain logic nằm ở services.

1. `dotnet new mvc -n FreshFarm.Web.Bff -o src/Web/FreshFarm.Web.Bff`
2. `dotnet sln src/FreshFarm.sln add src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj`
3. `dotnet add src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj reference src/BuildingBlocks/FreshFarm.Contracts/FreshFarm.Contracts.csproj`

Việc bạn làm tiếp trong BFF (tự code):
- Tạo `Areas/Admin` + `Areas/Seller` (Controllers/Views/Layout riêng)
- Dùng typed `HttpClient` gọi:
  - Identity: login/register, lấy token
  - Catalog: tạo listing, lấy product/listing
- BFF chỉ “orchestrate + view models”; không xử lý nghiệp vụ lõi

## 6) Set port cố định để dễ chạy local

Bạn có 2 cách (chọn 1):

- Cách A (nhanh): chạy với `ASPNETCORE_URLS`
  - Identity: `ASPNETCORE_URLS=http://localhost:5101`
  - Catalog: `ASPNETCORE_URLS=http://localhost:5102`
  - Ordering: `ASPNETCORE_URLS=http://localhost:5136`
  - BFF: `ASPNETCORE_URLS=http://localhost:5100`
- Cách B (đúng kiểu VS): chỉnh `Properties/launchSettings.json` của từng project

### 6.1) Cách B chi tiết (launchSettings.json)

Mỗi project sẽ có file:
- `src/Services/Identity/FreshFarm.Identity.API/Properties/launchSettings.json`
- `src/Services/Catalog/FreshFarm.Catalog.Api/Properties/launchSettings.json`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Properties/launchSettings.json`
- `src/Web/FreshFarm.Web.Bff/Properties/launchSettings.json`

Trong mỗi file, tìm profile `http` (hoặc tạo mới) và set `applicationUrl`:
- Identity: `http://localhost:5101`
- Catalog: `http://localhost:5102`
- Ordering: `http://localhost:5136`
- BFF: `http://localhost:5100`

Nếu có profile `https` thì giữ cũng được, nhưng khi làm Docker bạn nên ưu tiên chạy HTTP.

## 7) Run local (4 terminal)

- `dotnet run --project src/Services/Identity/FreshFarm.Identity.API/FreshFarm.Identity.Api.csproj`
- `dotnet run --project src/Services/Catalog/FreshFarm.Catalog.Api/FreshFarm.Catalog.Api.csproj`
- `dotnet run --project src/Services/Ordering/FreshFarm.Ordering.Api/FreshFarm.Ordering.Api.csproj`
- `dotnet run --project src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj`

## 7.1) Thêm endpoint `/health` cho Identity/Catalog/Ordering/BFF

Trong `Program.cs` của từng project, thêm 1 endpoint đơn giản:

```csharp
app.MapGet("/health", () => Results.Ok("ok"));
```

Files:
- `src/Services/Identity/FreshFarm.Identity.API/Program.cs`
- `src/Services/Catalog/FreshFarm.Catalog.Api/Program.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Program.cs`
- `src/Web/FreshFarm.Web.Bff/Program.cs`

Test nhanh trên trình duyệt:
- `http://localhost:5101/health`
- `http://localhost:5102/health`
- `http://localhost:5136/health`
- `http://localhost:5100/health`

## 7.2) Implement login/register + JWT ở Identity (gợi ý làm nhanh)

### Bước 1 — Thêm cấu hình JWT trong Identity `appsettings.json`

Trong `src/Services/Identity/FreshFarm.Identity.API/appsettings.json`, thêm section:
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:Key` (chuỗi dài >= 32 ký tự)

### Bước 2 — Bật JWT auth trong `Program.cs`

Trong `src/Services/Identity/FreshFarm.Identity.API/Program.cs`:
- Add Authentication `JwtBearer`
- `app.UseAuthentication()` trước `app.UseAuthorization()`

### Bước 3 — Tạo 2 endpoint

Tạo controller `Controllers/AuthController.cs` (hoặc minimal API) với:
- `POST /auth/register` (nhận Email/UserName/Password/Role)
- `POST /auth/login` (nhận Email/UserName + Password, trả JWT)

Gợi ý đơn giản (portfolio giai đoạn 1):
- Lưu user in-memory (vd `ConcurrentDictionary<string, UserRecord>`)
- Hash password bằng `PasswordHasher<T>`
- JWT chứa claim `sub`/`nameid` + `role`

Gợi ý DB (nếu bạn làm DB ngay):
- Với `PasswordHasher<T>`, `PasswordHash` là **string** → cột DB nên là `NVARCHAR(450)` (hoặc lớn hơn) và **không cần** `PasswordSalt` riêng.

## 7.3) Protect Catalog bằng JWT role/claims (Seller)

### Bước 1 — Catalog cũng bật JWT bearer giống Identity

Trong `src/Services/Catalog/FreshFarm.Catalog.Api/Program.cs`:
- cấu hình `AddAuthentication().AddJwtBearer(...)` giống hệt (Issuer/Audience/Key)
- thêm `app.UseAuthentication()` trước `app.UseAuthorization()`

### Bước 2 — Protect endpoint

Nếu bạn dùng Controller:
- gắn `[Authorize(Roles = "Seller")]` cho action `POST /seller/listings`

Nếu bạn dùng minimal API:
- `.RequireAuthorization(policy => policy.RequireRole("Seller"))`

### Bước 3 — Test

- Gọi `/auth/login` ở Identity lấy token
- Call Catalog endpoint với header: `Authorization: Bearer <token>`

## 7.4) BFF gọi Identity/Catalog/Ordering bằng typed `HttpClient` + trang login

### Bước 1 — Cấu hình base URL

Trong `src/Web/FreshFarm.Web.Bff/appsettings.json`, thêm:
- `Services:Identity:BaseUrl` = `https://localhost:7140`
- `Services:Catalog:BaseUrl` = `https://localhost:7245`
- `Services:Ordering:BaseUrl` = `https://localhost:7018`

### Bước 2 — Đăng ký typed HttpClient trong `Program.cs`

Trong `src/Web/FreshFarm.Web.Bff/Program.cs`:
- `builder.Services.AddHttpClient(...)` cho Identity/Catalog/Ordering (BaseAddress đọc từ config)

### Bước 3 — Tạo trang login (1 backend login, phân quyền theo role)

Tạo `Controllers/AccountController`:
- GET `/account/login` trả view
- POST `/account/login` gọi Identity `/auth/login`
- Lưu token (cách đơn giản: Session) và redirect theo role:
  - `Admin` → `/Admin/...`
  - `Seller` → `/Seller/...`
  - `Customer` → `/`

Để dùng Session:
- `builder.Services.AddDistributedMemoryCache();`
- `builder.Services.AddSession();`
- `app.UseSession();` (sau `UseRouting`, trước endpoints)

## 7.5) Dockerfile + `src/docker-compose.yml` (tuỳ chọn)

Bạn đã có hướng dẫn ở mục `## 10)`. Khi làm Docker, nhớ:
- BFF không gọi `localhost:5101` trong container → phải gọi `http://identity-api:8080` theo tên service trong compose.
- Set base URL bằng env vars nested key trong compose:
  - `Services__Identity__BaseUrl`
  - `Services__Catalog__BaseUrl`
  - `Services__Ordering__BaseUrl`

## 8) DB: bạn có 1 CSDL thì sao?

Để “đúng microservice” mà vẫn dễ làm:
- Dùng **1 SQL Server** nhưng tạo **nhiều database**: `IdentityDb`, `CatalogDb`, `OrdersDb`...
- Mỗi service 1 connection string + migrations riêng.

Nếu bạn chưa tách kịp:
- Vẫn có thể chạy multi-service với 1 DB, nhưng tránh join/ghi chéo bảng giữa service (dễ bị đánh giá “distributed monolith”).

### 8.1) Bạn đã có script DB lớn `docs/final3.sql` thì xử lý sao?

Trong repo của bạn đã có file: `docs/final3.sql` (DB `testFreshFarmDB1`, rất nhiều bảng).

Khuyến nghị (microservices đúng hướng):
- **Đừng cố “tách hết bảng” ngay**.
- Bắt đầu từ **Identity service**: tạo `IdentityDb` (database mới) chỉ chứa các bảng liên quan đăng nhập/xác thực.

Trong `final3.sql` hiện đã có sẵn các bảng identity quan trọng:
- `Users` (thông tin profile)
- `UserAuth` (lock/MFA; phần password tuỳ cách bạn làm)
- `UserSession` (RefreshToken)
- `Role`, `Permission`, `RolePermission`
- `UserAdmin` (nếu bạn muốn tách admin riêng; hoặc hợp nhất về user/role sau)

Hai hướng triển khai (chọn 1):
- **Hướng A (đúng microservices hơn)**: tạo database mới `IdentityDb` và *copy* (trích) các `CREATE TABLE` identity từ `final3.sql` sang 1 file mới (vd `docs/identitydb.sql`) rồi chạy.
- **Hướng B (tạm thời để demo nhanh)**: vẫn dùng chung DB `testFreshFarmDB1` nhưng Identity service chỉ truy cập nhóm bảng identity; các service khác *không join/không FK* sang bảng identity (chỉ lưu `UserId` và verify JWT).

Lưu ý quan trọng:
- Trong `Users` của `final3.sql` vẫn có cột `Password` (nvarchar). Khi làm Identity service, bạn nên **bỏ dùng cột này** và dùng `UserAuth` để thể hiện bạn làm đúng security.
- Nếu bạn dùng `PasswordHasher<TUser>` (Identity style), bạn có thể đổi `UserAuth.PasswordHash` sang kiểu chuỗi (`NVARCHAR(450)` hoặc lớn hơn) và bỏ `PasswordSalt`.

## 9) Checklist để người khác nhìn vào biết là microservices

- [ ] Có nhiều executable độc lập: `FreshFarm.Identity.Api`, `FreshFarm.Catalog.Api`, `FreshFarm.Ordering.Api`, `FreshFarm.Web.Bff`
- [ ] Mỗi service có `/health` và chạy port riêng
- [ ] Identity cấp JWT; Catalog authorize theo role/claims
- [ ] BFF gọi services qua HTTP (không reference trực tiếp code domain của services)
- [ ] (Tốt nhất) DB per service (hoặc ít nhất per-service DbContext + không share entity)

## 10) Docker/Docker Compose (chưa có file sẵn, bạn tự tạo)

Hiện repo **chưa có** `Dockerfile` hoặc `docker-compose.yml`. Nếu bạn muốn demo “microservices đúng bài” khi xin việc, bạn nên thêm Compose để chạy 4 app + (tuỳ chọn) SQL Server.

### 10.1) Dockerfile cho từng service (mẫu hướng dẫn)

Đặt `Dockerfile` ngay trong mỗi project:
- `src/Services/Identity/FreshFarm.Identity.API/Dockerfile`
- `src/Services/Catalog/FreshFarm.Catalog.Api/Dockerfile`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Dockerfile`
- `src/Web/FreshFarm.Web.Bff/Dockerfile`

Gợi ý nội dung (multi-stage):
- Stage 1: `mcr.microsoft.com/dotnet/sdk:8.0` → `dotnet restore` + `dotnet publish -c Release -o /app/publish`
- Stage 2: `mcr.microsoft.com/dotnet/aspnet:8.0` → copy publish + `ENTRYPOINT ["dotnet", "...dll"]`

Lưu ý:
- Trong container ưu tiên chạy **HTTP** (không cần HTTPS dev cert).
- Mỗi app expose port nội bộ 8080 (hoặc 80) và map ra host port 5100/5101/5102/5103.

### 10.2) `docker-compose.yml` (chạy 4 app)

Tạo file: `src/docker-compose.yml`

Compose nên có 4 service:
- `identity-api` → build từ `src/Services/Identity/FreshFarm.Identity.API`
- `catalog-api` → build từ `src/Services/Catalog/FreshFarm.Catalog.Api`
- `ordering-api` → build từ `src/Services/Ordering/FreshFarm.Ordering.Api`
- `web-bff` → build từ `src/Web/FreshFarm.Web.Bff`

Ports gợi ý:
- `web-bff`: `5100:8080`
- `identity-api`: `5101:8080`
- `catalog-api`: `5102:8080`
- `ordering-api`: `5103:8080`

### 10.3) “Kết nối” giữa các container (quan trọng)

Trong Docker network, BFF không gọi `http://localhost:5101` được. BFF phải gọi theo **tên service**:
- Identity base URL: `http://identity-api:8080`
- Catalog base URL: `http://catalog-api:8080`
- Ordering base URL: `http://ordering-api:8080`

Vì vậy bạn nên cấu hình URL bằng `appsettings.json` hoặc env vars trong BFF, ví dụ:
- `Services__Identity__BaseUrl=http://identity-api:8080`
- `Services__Catalog__BaseUrl=http://catalog-api:8080`
- `Services__Ordering__BaseUrl=http://ordering-api:8080`

### 10.4) (Tuỳ chọn) Thêm SQL Server vào compose

Bạn có thể thêm container SQL Server:
- Image: `mcr.microsoft.com/mssql/server:2022-latest`
- Expose: `1433:1433`
- Env: `ACCEPT_EULA=Y`, `MSSQL_SA_PASSWORD=...`

Connection string trong container sẽ trỏ về host `sqlserver` (tên service), ví dụ:
- `Server=sqlserver;Database=IdentityDb;User Id=sa;Password=...;TrustServerCertificate=True`

### 10.5) Lệnh chạy

Chạy tại repo root:
- `docker compose -f src/docker-compose.yml up --build`
- `docker compose -f src/docker-compose.yml down`
