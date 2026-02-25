# Roadmap Sua Dan Seller/Admin (.NET 8)

## Muc tieu
- Giu baseline build/run on dinh cho BFF.
- Khong xoa code legacy Seller hien co.
- Migrate dan tung module, khong lam big-bang.

## Trang thai hien tai
- Da bat che do mac dinh: khong compile `Areas/Seller/**` de tranh gay build.
- Co the bat lai Seller legacy tam thoi bang:
  - `dotnet build src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj -p:EnableLegacySeller=true`
- Tien do go legacy da lam:
  - [x] Goi bo filter cu `AdminAuthorize`, `NoCache` tren Seller controllers.
  - [x] Doi sang attribute core: `Authorize` + `ResponseCache`.
  - [x] Go dependency SignalR cu trong `OrderController`.
  - [x] Go dependency `Newtonsoft.Json` trong `WarehouseController` (chuyen `System.Text.Json`).
  - [x] Hoan tat phase module `Product + Category + Unit`:
    - Seller controllers da chuyen qua `IHttpClientFactory` goi `Catalog API`.
    - Khong con `FreshFram.*` / `FreshFarmDBEntities` trong 3 controller + views cua cum nay.
    - Da bo sung endpoint Catalog API cho `Products/Categories/Units` de phuc vu CRUD.
  - [x] Hoan tat phase module `Order` theo service-first:
    - Seller `OrderController` + views da chuyen sang goi `Ordering API` (khong EF truc tiep).
    - Da bo sung endpoint admin-order trong `Ordering API` cho paged/search/detail/update/delete/statistics.
    - Khong con `FreshFram.*` / `FreshFarmDBEntities` trong cum `Seller/Order`.
    - Da siet auth role `Seller` cho admin-order endpoints va Catalog CRUD endpoints.
  - [x] Hoan tat phase module `Customer + Coupon` theo service-first:
    - Seller `CustomerController` da goi `Identity API` + `Ordering API` (khong EF truc tiep).
    - Seller `CouponController` da goi `Ordering API` + `Identity API` cho CRUD/send/validate/statistics.
    - Da bo sung endpoint admin-customer trong `Identity API`.
    - Da bo sung endpoint admin-coupon + customer-order-metrics trong `Ordering API`.
    - Khong con `FreshFram.*` / `FreshFarmDBEntities` trong cum `Seller/Customer` va `Seller/Coupon`.
  - [x] Hoan tat phase module `Warehouse + Shipping` theo service-first (MVP):
    - Seller `WarehouseController` da goi `Catalog API` (khong EF truc tiep).
    - Seller `ShippingController` da goi `Ordering API` (khong EF truc tiep).
    - Da bo sung `Catalog API`: `Controllers/WarehouseAdminController.cs`.
    - Da bo sung `Ordering API`: `Controllers/ShippingAdminController.cs`.
    - Da cap nhat views/models Seller cho cum Warehouse + Shipping sang namespace moi.
    - Khong con `FreshFram.*` / `FreshFarmDBEntities` / `System.Data.Entity` trong cum `Seller/Warehouse` va `Seller/Shipping`.
    - Gioi han MVP hien tai:
      - Warehouse transaction dang luu tam in-memory trong service (chua persistence day du theo lo/HSD).
      - Shipping chua bat lai phan cong shipper chi tiet (delivery staff assignment tra rong).
  - [ ] `FreshFram.*` va `FreshFarmDBEntities` van con o cac module Seller khac -> tiep tuc migrate theo module.

## Phase 1 - Baseline on dinh (1-2 ngay)
- Muc tieu: build core BFF (Home/Account/Cart/Checkout) on dinh.
- Viec lam:
  - Khoa policy: Seller legacy chi test khi dung flag `EnableLegacySeller=true`.
  - Don dep noise vendor (`wwwroot/lib`) va chot line-ending policy.
  - Chot check script static route/view (Seller) de dung lai khi re-enable.
- Definition of Done:
  - Build core BFF pass.
  - Khong phat sinh diff lon khong can thiet o `wwwroot/lib`.

## Phase 2 - Chuan hoa dependency legacy (2-4 ngay)
- Muc tieu: giam blocker compile cua Seller.
- Viec lam:
  - Lap danh sach type legacy dang dung: `FreshFram.*`, `FreshFarmDBEntities`, `AdminAuthorize`, `NoCache`.
  - Tao bridge package/assembly noi bo hoac thay bang type moi.
  - Loai bo dependency khong phu hop .NET Core:
    - `Microsoft.AspNet.SignalR` (thay bang no-op hoac SignalR Core).
    - `System.Data.Entity` (thay dan bang EF Core).
- Definition of Done:
  - Co danh sach mapping type cu -> type moi.
  - Khong con dependency legacy "khong ton tai" o module uu tien.

## Phase 3 - Re-enable module theo thu tu (1-2 tuan)
- Thu tu de xuat:
  1. Product + Category + Unit [DA XONG]
  2. Order [DA XONG]
  3. Customer + Coupon [DA XONG]
  4. Warehouse + Shipping [DA XONG - MVP]
  5. Report + Review + SupportChat + Setting + User
- Cach lam cho moi module:
  - Bat module trong compile.
  - Sua controller -> service call (khong truy cap DB truc tiep trong BFF).
  - Sua view route/model theo controller moi.
  - Chay test tay URL + smoke checklist.

## Phase 3.5 - Breakdown chi tiet cho cum 5 (ra lenh dan)
- Muc tieu: chia nho de lam theo lot nho, moi lot co DoD ro rang.
- Nguyen tac chung:
  - Service-first, khong truy cap DB truc tiep trong BFF.
  - Giu route/view compatibility (khong pha UI hien co neu khong can).
  - Moi lot xong phai quet static: `FreshFram.*`, `FreshFarmDBEntities`, `System.Data.Entity`.

### Lot 5.0 - Inventory + mapping (khong sua logic)
- Scope:
  - Kiem ke action trong 5 controller: `Report/Review/SupportChat/Setting/User`.
  - Map model/view can giu tuong thich.
- Files chinh:
  - `Areas/Seller/Controllers/{ReportController,ReviewController,SupportChatController,SettingController,UserController}.cs`
  - `Areas/Seller/Views/Report/*`, `Views/Review/*`, `Views/SupportChat/*`, `Views/Setting/*`, `Views/User/*`
- DoD:
  - Co bang mapping action -> API endpoint du kien.
  - Co danh sach view-model field toi thieu cho tung view.
- Trang thai:
  - [x] Hoan tat inventory + mapping tai `LOT_5_0_INVENTORY_MAPPING.md`.
- Cau lenh giao viec mau:
  - `Lam lot 5.0: inventory + mapping cho cum 5`

### Lot 5.1 - Setting + User (uu tien de, it phu thuoc)
- Scope:
  - Viet lai `SettingController`, `UserController` theo service-first.
  - Tao/bo sung model Seller local neu can.
  - Chinh views Setting/User theo namespace model moi.
- DoD:
  - Khong con `FreshFram.*` / `FreshFarmDBEntities` trong 2 controller + views lien quan.
  - Route chinh van hoat dong: `Setting/Index`, `User/ManageUsers`.
- Cau lenh giao viec mau:
  - `Lam lot 5.1: migrate Setting + User service-first`

### Lot 5.2 - SupportChat
- Scope:
  - Viet lai `SupportChatController` theo API service.
  - Chuan hoa JSON response cho cac action list/detail/mark-read/close.
  - Chinh view `SupportChat/Index.cshtml` theo model moi.
- DoD:
  - Khong con dependency legacy trong SupportChat controller/view.
  - Luong `Conversations -> Messages -> MarkAsRead/Close` chay duoc theo API.
- Cau lenh giao viec mau:
  - `Lam lot 5.2: migrate SupportChat service-first`

### Lot 5.3 - Review
- Scope:
  - Viet lai `ReviewController` theo API service (manage/reported/resolve/delete/reply/update).
  - Chuan hoa binding cho bo loc + paging.
  - Chinh `ManageReview.cshtml`, `ReportedReviews.cshtml` theo model namespace moi.
- DoD:
  - Khong con dependency legacy trong Review controller/views.
  - Action quan trong chay duoc: filter, resolve report, toggle visibility, reply.
- Cau lenh giao viec mau:
  - `Lam lot 5.3: migrate Review service-first`

### Lot 5.4 - Report part 1 (read-only pages truoc)
- Scope:
  - Tach `ReportController` theo nhom page read-only:
    - `Customer`, `Order`, `Revenue`, `Product`, `Shipping`, `Review`
  - Chuyen logic query sang API service, bo EF truc tiep.
- DoD:
  - 6 page report render duoc bang data service.
  - Khong con `FreshFarmDBEntities` trong report read-path.
- Cau lenh giao viec mau:
  - `Lam lot 5.4: migrate Report read pages service-first`

### Lot 5.5 - Report part 2 (export + action con lai)
- Scope:
  - Migrate cac action export excel.
  - Migrate action write con lai (neu co) nhu delete review trong report context.
  - Toi uu timeout/fallback de tranh vo luong export lon.
- DoD:
  - Export action chay thong suot qua service API.
  - Khong con dependency legacy trong toan bo `ReportController`.
- Cau lenh giao viec mau:
  - `Lam lot 5.5: migrate Report export + remaining actions`

### Lot 5.6 - Hardening + auth + static audit cho cum 5
- Scope:
  - Ra soat auth role `Seller` cho Catalog CRUD lien quan + admin-order link trong cum 5.
  - Quet static toan bo Seller: legacy using/type.
  - Chot checklist regression route/view.
- DoD:
  - Cum 5 khong con `FreshFram.*`, `FreshFarmDBEntities`, `System.Data.Entity`.
  - Auth nhat quan theo role/policy.
  - Co log ket qua quet static + danh sach route smoke test.
- Cau lenh giao viec mau:
  - `Lam lot 5.6: hardening auth + static audit cum 5`

## Phase 4 - Bat dau Admin (song song voi phase 3 sau khi core on)
- Muc tieu: co Admin area chay duoc o muc MVP.
- Scope MVP:
  - Dashboard
  - Product
  - Order
- Nguyen tac:
  - Dung chung layout/pattern voi Seller da migrate.
  - Controller Admin moi phai theo service-first (khong EF truc tiep).

## Phase 5 - Tach microservice (sau khi flow on)
- Tach module nang:
  - Warehouse
  - Shipping
  - Report read model
- BFF luc nay chi giu API aggregation va UI orchestration.

## Checklist tuan nay (de bat dau ngay)
1. Chay local build core BFF khong Seller legacy.
2. Chot line-ending policy (`.gitattributes`) de khong bi churn vendor.
3. Chon module re-enable dau tien: `Product`.
4. Tao ticket nho theo cong thuc: `1 module = 1 PR`.

## Backlog go dep legacy (uu tien ngay)
1. Product + Category + Unit:
   - [x] Bo `FreshFarmDBEntities`.
   - [x] Goi Catalog service qua BFF/HttpClient.
2. Order:
   - [x] Bo `FreshFarmDBEntities`.
   - [x] Goi Ordering service.
   - [x] Siet role/policy cho admin-order endpoints + Catalog CRUD (`SellerOnly`).
3. Customer + Coupon:
   - [x] Bo truy cap DB truc tiep.
   - [x] Chuyen sang Identity/Ordering APIs.
