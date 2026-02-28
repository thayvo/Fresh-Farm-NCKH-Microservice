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
- Trang thai:
  - [x] Hoan tat migrate service-first cho Setting + User.
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
- Trang thai:
  - [x] Hoan tat migrate service-first cho SupportChat.
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
- Trang thai:
  - [x] Hoan tat migrate service-first cho Review.
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
- Trang thai:
  - [x] Hoan tat migrate service-first cho read-path `Customer/Order/Revenue/Product/Shipping/Review`.
  - [x] Da bo sung `Ordering API`: `Controllers/ReportsAdminController.cs` (read endpoints + report-review delete).
  - [x] Da chuyen Report views sang model namespace local `FreshFarm.Web.Bff.Areas.Seller.Models.*`.
  - [x] Export duoc tach rieng va hoan tat o lot 5.5.
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
- Trang thai:
  - [x] Hoan tat migrate export actions qua service API:
    - BFF `ReportController` export actions da proxy file download tu `Ordering API`.
    - `Ordering API` da bo sung `api/orders/admin/reports/*/export` (CSV mo duoc bang Excel).
  - [x] Report review delete da qua service API (`DELETE /api/orders/admin/reports/reviews/{id}`).
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
- Trang thai:
  - [x] Cum 5 dat DoD:
    - Khong con legacy pattern trong `Setting/User/SupportChat/Review/Report` controllers+views.
    - Auth nhat quan `Seller` cho BFF cum 5 va `SellerOnly` cho service APIs lien quan.
  - [x] Da tao log hardening + smoke checklist:
    - `LOT_5_6_HARDENING_AUDIT.md`
  - [!] Static audit toan bo Seller area van con legacy o modules ngoai scope lot 5:
    - `Loyalty/Status` (+ views lien quan).
- Cau lenh giao viec mau:
  - `Lam lot 5.6: hardening auth + static audit cum 5`

## Re-plan sau system audit (2026-02-26)
- Ket luan huong:
  - Khong sai huong: van giu `service-first`.
  - Can doi thu tu uu tien: dong gap runtime/he thong truoc khi mo module moi (Admin hoac microservice tiep).

## Phase 4 - System Stabilization (uu tien cao nhat)

### Lot 4.1 - Runtime config baseline
- Ly do:
  - Config mau dang thieu key quan trong (base url service/JWT/connection string) -> kho boot local on dinh.
- Scope:
  - Dong bo `appsettings.Development.json.example` cho BFF/Identity/Catalog/Ordering.
  - Bo sung file example cho Ordering (`appsettings.Development.json.example`) neu chua co.
  - Cap nhat `src/README.md` theo config va route hien tai.
- DoD:
  - Nguoi moi clone repo co du file mau de set env khong doan.
  - Khong con chenhlech lon giua docs va code hien tai.
- Trang thai:
  - [x] Da chuan hoa config examples:
    - `src/Web/FreshFarm.Web.Bff/appsettings.Development.json.example` (bo sung `Services:{Identity,Catalog,Ordering}:BaseUrl`).
    - `src/Services/Identity/FreshFarm.Identity.API/appsettings.Development.json.example` (bo sung `ConnectionStrings:IdentityDB`).
    - `src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.Development.json.example` (bo sung `ConnectionStrings:FreshFarmCatalogDB`).
    - Tao moi `src/Services/Ordering/FreshFarm.Ordering.Api/appsettings.Development.json.example`.
  - [x] Da cap nhat `src/README.md` cho dung key config nested (`Services:Identity:BaseUrl`...) va runtime 4 services.
- Lenh giao viec:
  - `Lam lot 4.1: chuan hoa config examples + README runtime`

### Lot 4.2 - Seller compile gate
- Ly do:
  - `EnableLegacySeller=false` dang remove toan bo `Areas/Seller/**` khoi compile mac dinh.
- Scope:
  - Chuyen gate tu "tat toan bo Seller" sang "chi tat cum legacy chua migrate".
  - Dam bao cac cum da migrate (1 -> 5.6) compile duoc mac dinh.
- DoD:
  - Build mac dinh co Seller migrated modules.
  - Cac module chua migrate duoc gate ro rang (khong anh huong module da xong).
- Trang thai:
  - [x] Da doi gate theo module trong `src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj`:
    - Khong con `Compile Remove="Areas/Seller/**/*.cs"` global.
    - Chi remove compile/view cho cum legacy chua migrate:
      - Controllers: `Feedback`, `Delivery`, `Loyalty`, `Status`.
      - Views folders tuong ung.
  - [!] UNCONFIRMED runtime/build:
    - Chua verify build local do environment assistant khong co `dotnet`.
- Lenh giao viec:
  - `Lam lot 4.2: doi compile gate Seller theo module`

### Lot 4.3 - Static assets + layout admin
- Ly do:
  - `_LayoutAdmin` dang goi script khong co trong workspace:
    - `~/Scripts/jquery-3.7.0.min.js`
    - `~/Scripts/jquery.signalR-2.4.3.min.js`
    - `~/signalr/hubs` (route chua co trong .NET Core stack hien tai)
- Scope:
  - Chot huong script:
    - doi sang `~/lib/jquery/dist/jquery.min.js` (co san), va
    - tam bo signalr legacy references neu chua co hub core.
  - Dam bao cac view Seller khong vo JS vi missing static files.
- DoD:
  - Khong con 404 tai static scripts o layout admin.
  - Trang Seller co the render + bind script co ban on dinh.
- Trang thai:
  - [x] Da bo script references khong ton tai trong `_LayoutAdmin.cshtml`:
    - Bo `~/Scripts/jquery-3.7.0.min.js` -> dung `~/lib/jquery/dist/jquery.min.js`.
    - Bo `~/Scripts/jquery.signalR-2.4.3.min.js` va `~/signalr/hubs`.
  - [x] Khong con route/script 404 tu layout admin cho cac references tren.
- Lenh giao viec:
  - `Lam lot 4.3: fix layout admin scripts + bo signalr legacy reference`

### Lot 4.4 - SupportChat runtime mode
- Ly do:
  - `support-chat-admin.js` dang phu thuoc `$.connection.supportChatHub` (legacy SignalR), trong khi backend hien tai chua co hub route tuong ung.
- Scope:
  - Chon 1 trong 2:
    - Polling-only tam thoi (giu read/list/close/mark-read qua REST), hoac
    - Implement SignalR Core hub day du.
  - Cap nhat script theo huong da chon (co guard khi khong co hub).
- DoD:
  - SupportChat chay on dinh, khong throw JS error do hub undefined.
- Trang thai:
  - [x] Da chot huong polling-safe fallback (tam thoi khong real-time push):
    - `support-chat-admin.js` co guard khi khong co `$.connection`/`supportChatHub`.
    - Tu dong disable input gui tin nhan khi realtime unavailable.
    - Giu cac luong REST: conversations/messages/details/close/mark-read.
  - [!] Realtime hub mode chua bat (se can lot rieng neu muon day du send/typing/reaction qua hub).
- Lenh giao viec:
  - `Lam lot 4.4: chot va implement runtime mode cho SupportChat`

### Lot 4.5 - Persistence hardening cho endpoints tam
- Ly do:
  - Dang con in-memory state o API admin:
    - Ordering: support chat/review/report delete marker.
    - Identity: settings/avatar map.
- Scope:
  - Chuyen cac state tam nay ve persistence DB (hoac doc/ghi qua bang phu tro).
  - Chot migration script DB cho cac bang moi (neu can).
- DoD:
  - Khong con `ConcurrentDictionary` cho business state chinh.
  - Restart service khong mat du lieu nghiep vu quan trong.
- Trang thai:
  - [x] Da hoan tat huong file-backed persistence (interim) de chong mat state khi restart:
    - [x] `Identity API`:
      - `AdminSettingsController`: persisted `App_Data/identity.admin-store-settings.json`.
      - `AdminUsersController`: persisted `App_Data/identity.admin-users.avatars.json`.
    - [x] `Ordering API`:
      - `ReportsAdminController`: persisted `App_Data/ordering.reports.deleted-review-ids.json`.
      - `SupportChatAdminController`: persisted `App_Data/ordering.support-chat.store.json`.
      - `ReviewsAdminController`: persisted `App_Data/ordering.reviews.store.json`.
  - [x] Dat muc tieu 4.5 interim: restart service khong mat state cho cac endpoint tam da inventory.
  - [x] Da chot 4.5.4 o muc de xuat schema + migration/rollback drafts:
    - `docs/LOT_4_5_4_DB_SCHEMA_MIGRATION_ROLLBACK_PLAN.md`
    - `docs/lot-4.5.4-ordering-up.sql`
    - `docs/lot-4.5.4-ordering-down.sql`
    - `docs/lot-4.5.4-identity-up.sql`
    - `docs/lot-4.5.4-identity-down.sql`
    - `docs/lot-4.5.4-sql-complete.md`
  - [x] Da cap nhat code controllers sang DB-backed (local code-level) va da verify schema/delta tren DB that.
  - [x] Da bo sung va chay hotfix index ContactMessages tren Ordering:
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.4.5.4.hotfix-indexes.sql`
  - [x] Da bo sung bo script triage runtime Seller (orders/revenue/product):
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.seller-runtime-diagnostics.sql`
    - Da mo rong script triage voi `Sanitized metrics` de doi chieu cung dieu kien loc report API.
  - [x] Da bo sung script soi outlier quantity:
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.quantity-outlier-diagnostics.sql`
  - [x] Da bo sung ke hoach migrate du lieu khach hang tu `final3.sql` sang microservices:
    - `docs/LOT_4_5_6_FINAL3_CUSTOMER_MIGRATION_2026-02-28.md`
  - [i] Chi tiet tracking lot:
    - `LOT_4_5_PERSISTENCE_HARDENING.md`
    - `docs/LOT_4_5_4_DB_SCHEMA_MIGRATION_ROLLBACK_PLAN.md`
    - `docs/LOT_4_5_5_RUNTIME_SMOKE_2026-02-28.md`
- Lenh giao viec:
  - `Lam lot 4.5.6: migrate customer data tu final3.sql theo plan + verify runtime`

### Lot 4.6 - Refactor Seller View Integration & Controller Mapping [HIGH]
- Status:
  - [~] In-Progress
- Priority:
  - High
- Context:
  - Source code da migrate tu legacy project.
  - Views Seller hien van co nguy co loi `404` hoac `Binding Error` do sai lech route/controller mapping.
- Objective:
  - Re-wiring toan bo ket noi giua Seller Views va controller moi.
- Constraint:
  - Tuyet doi khong thay doi UI/UX (giu nguyen HTML/CSS/JS hien tai).
  - Chi dieu chinh `@model` trong Razor View va endpoint/controller mapping.

#### 4.6.1 Analysis & Route Mapping (Quet + Phan tich)
- Status:
  - [x] Hoan tat vong quet baseline sau khi views bi copy lai tu legacy:
    - Da inventory mismatch `@model` namespace, `area=\"Admin\"`, layout path, `Request/Session` o Razor, va `@page` ambiguity.
    - Da doi chieu voi Seller controllers/service-first routes hien tai.
- Scope:
  - Quet toan bo `Areas/Seller/Views/**/*.cshtml` vua migrate.
  - Truy vet toan bo:
    - `<form ...>`
    - `$.ajax(...)`
    - `@Url.Action(...)`
    - `@Html.BeginForm(...)`
  - Doi chieu `@model` dau file voi DTO/ViewModel hien hanh.
- DoD:
  - Co inventory file-by-file cho route target + model target.
  - Co danh sach mismatch (route/controller/model binding) theo muc uu tien.

#### 4.6.2 Controller Synchronization (Dong bo controller)
- Status:
  - [~] Da dong bo tiep theo runtime route/binding:
    - `User/ManageUsers` da bind lai `searchTerm` qua `ViewBag.SearchTerm + Context.Request.Query`.
    - Da khoi phuc views thieu phu thuoc action:
      - `Review/ReportedReviews.cshtml`
      - `Warehouse/TransactionDetails.cshtml`
    - Da bo sung action thieu o BFF:
      - `FeedbackController.GetFeedback(int id)` de modal chi tiet `Feedback/Index` goi duoc endpoint hop le.
    - Da bo sung endpoint detail ben service:
      - `Ordering API` `GET /api/orders/admin/feedbacks/{id}` (`FeedbacksAdminController.GetById`).
    - Da dong bo paging/filter params giua view va controller:
      - `FeedbackController.Index(int page, int pageSize, string? q, string? status)` da bind query keys va cap `ViewBag` paging (`CurrentPage/PageSize/TotalRecords/TotalPages`).
- Scope:
  - Cap nhat action methods de nhan dung tham so ma views cu dang submit.
  - Dam bao `ViewData`/`ViewBag` cap du du lieu cho UI cu (dropdown/table/filter/paging).
- DoD:
  - Khong con action nao mismatch ve query/form keys chinh.
  - Du lieu phu tro view (nhat la dropdown/filter lists) day du va on dinh.

#### 4.6.3 Endpoint Update & Data Binding (Sua ket noi)
- Status:
  - [~] Da apply batch re-wiring lon cho Seller views (giu UI):
    - Chuan hoa `area=\"Seller\"` va layout path sang `Areas/Seller`.
    - Chuan hoa `@model` sang `FreshFarm.Web.Bff.Areas.Seller.Models.*`.
    - Bo helper legacy khong tuong thich Razor Core (`System.Web.Mvc`, `IHtmlString`, `Scripts.Render`, `Styles.Render`).
    - Sua `@page` ambiguity: `Loyalty/History`, `Report/Review`.
    - Sua navbar/layout de dung auth context hien tai (`User.Identity`) va partial name.
    - Sua route sai controller/action gay 404:
      - `Unit/{Create,Edit,Index}`: breadcrumb `Dashboard` controller cu -> `Home/Dashboard` (`area=Seller`).
      - `Review/ManageReview`: link `Product/Details` (khong ton tai) -> `Product/Edit` (`area=Seller`).
    - `Feedback/Index`: link phan trang da giu query `q/status` khi chuyen trang, tranh mat state.
    - Da xoa artifacts legacy MVC trong Seller views:
      - `Areas/Seller/Views/web.config`
      - `Areas/Seller/Views/web.config.copy`
    - Da bo sung compatibility mapping cho `Order/ManageOrders` (JS render fallback key casing):
      - Ho tro song song `OrderID/orderId`, `OrderCode/orderCode`, `TotalAmount/totalAmount/total`, `Status/status` de tranh hien thi `0đ`/rong cot khi contract drift.
    - Da harden `Ordering API` report quantity aggregation sau triage DB:
      - `ReportsAdminController`: SUM quantity dung `long`, clamp payload int bang `ToSafeNonNegativeInt(long)` de tranh overflow 500 o `Report/Product` va `Report/Revenue`.
    - Da bo sung report data-quality guard:
      - Loai cac `OrderDetail` bat thuong khi tinh report (`Quantity <= 0`, `Quantity > 10000`, `UnitPrice <= 0`, `ProductId` khong hop le) de tranh meo thong ke.
    - Da bo sung request validation guard o `OrdersController.Create`:
      - Chan tao moi item co `Quantity` ngoai khoang `1..10000` (DTO + server check).
  - [!] Con buoc verify build/runtime tren may local (assistant environment khong co `dotnet`).
  - [x] Da bo sung static integrity gate script:
    - `scripts/seller_static_smoke.sh`
    - Check legacy patterns + explicit `Url.Action/BeginForm` mapping + `@page` ambiguity.
- Scope:
  - Chuan hoa action/controller names trong Razor logic ve route moi.
  - Mapping lai `name` attributes cua input de khop ASP.NET Core model binding.
- DoD:
  - Khong con 404 do route mapping sai o Seller views da migrate.
  - Khong con binding error do mismatch `name -> action params/view model`.

- Lenh giao viec:
  - `Lam lot 4.6: refactor seller view integration + controller mapping, giu nguyen UI`

## Phase 5 - Go cum legacy con lai (ngoai scope lot 5)
- Trang thai:
  - [x] Module 1 `AdminAccount` da migrate service-first (`Identity API` login + cookie/session bridge).
  - [x] Module 2 `Home` da migrate service-first (`Ordering API` dashboard + `Identity API` profile/password path).
  - [x] Module 3 `Feedback` da migrate service-first (`Ordering API` feedback endpoints + BFF bridge).
  - [x] Module 4 `Delivery` da migrate service-first (`Ordering API` shipping report + order status update bridge).
  - [x] Module 5 `Loyalty` da migrate service-first (`Ordering API` loyalty dashboard/users/history/config/adjust/sync-award + BFF bridge).
  - [x] Module 6 `Status` da migrate service-first (`Ordering API` statuses/status-types CRUD + BFF bridge).
- Scope uu tien tiep theo:
  1. `Phase 5 complete` (khong con module Seller legacy nao).
- Muc tieu:
  - Xoa dut diem `FreshFram.*`, `FreshFarmDBEntities`, `System.Data.Entity` trong toan Seller area.
- Ket qua static audit hien tai:
  - [x] `rg -n "FreshFram|FreshFarmDBEntities|System.Data.Entity|System.Web.Mvc|Scripts.Render|Styles.Render" Areas/Seller/{Controllers,Views}` -> khong con match runtime files.
- Lenh giao viec mau:
  - `Lam static audit + smoke test toan Seller area sau khi complete phase 5`

## Phase 6 - Admin area MVP + tach service tiep
- Dieu kien vao phase:
  - Phase 4 hoan tat (runtime/stability ok).
  - Phase 5 dat muc "Seller area clean legacy".
- Scope:
  - Admin Dashboard/Product/Order theo service-first.
  - Sau do moi tach service nang (Warehouse/Shipping/Report read model) neu can.
