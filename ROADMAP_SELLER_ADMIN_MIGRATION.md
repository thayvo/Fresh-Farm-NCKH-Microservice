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

## Danh gia lai roadmap duoi goc nhin san TMĐT (2026-03-07)
- Ket luan tong quat:
  - Huong hien tai la DUNG cho bai toan san TMĐT multi-seller:
    - service-first,
    - tenant isolation,
    - seller runtime on dinh,
    - admin ops duoc mo dan theo lane rieng.
  - Tuy nhien roadmap hien tai dang manh o `UI/admin CRUD + quan sat van hanh`, nhung chua du day cac truc nen tang cua san TMĐT.
- Cac khoang trong phai bo sung:
  1. `Finance/Settlement/Commission/Refund`:
     - neu khong co ledger doi soat va payout thi chua the goi la san TMĐT van hanh day du.
  2. `Returns & After-sales`:
     - doi tra/hoan tien/tranh chap sau giao hang hien chua duoc dat thanh mot truc rieng.
  3. `Merchant lifecycle & compliance`:
     - KYC/mo shop/duyet seller/chinh sach phi la bat buoc cho san multi-seller.
  4. `Catalog quality + searchability`:
     - attributes theo danh muc, completeness score, search synonym/tag/facet.
  5. `Risk/Fraud/Abuse control`:
     - don ao, voucher abuse, fake review, spam account, payment risk.
  6. `Ops audit & governance`:
     - admin action log, moderation audit, settlement audit, notification policy.
  7. Dac thu domain `nong san/thuc pham tuoi`:
     - lo hang, han su dung, FIFO/FEFO, truy xuat nguon goc, quality recall.
- Dieu chinh uu tien:
  - Khong nen day qua nhanh vao `Campaigns/Ads` truoc khi co `Settlement + Refund + Compliance`.
  - `Trung tam thong bao` la module phu tro, khong phai core luong song con.
  - Sau `Catalog moderation`, huong dung chat san TMĐT hon la `Finance/Settlement + Returns/Refunds + Disputes`, roi moi toi `Campaigns`.

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
  - [x] Da bo sung bo script thuc thi lot 4.5.6 (Final3 -> microservices):
    - `docs/FreshFarmIdentityDb/FreshFarmIdentityDb.2026-02-28.4.5.6.final3-import.sql`
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.4.5.6.final3-import.sql`
    - `docs/FreshFarmIdentityDb/FreshFarmIdentityDb.2026-02-28.4.5.6.final3-verify.sql`
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.4.5.6.final3-verify.sql`
    - `docs/LOT_4_5_6_EXECUTION_RUNBOOK_2026-02-28.md`
  - [i] Chi tiet tracking lot:
    - `LOT_4_5_PERSISTENCE_HARDENING.md`
    - `docs/LOT_4_5_4_DB_SCHEMA_MIGRATION_ROLLBACK_PLAN.md`
    - `docs/LOT_4_5_5_RUNTIME_SMOKE_2026-02-28.md`
- Lenh giao viec:
  - `Chay 4 script lot 4.5.6 (import + verify) tren staging, sau do chay lai seller runtime diagnostics + smoke UI`

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
    - Da harden auth token propagation cho Seller BFF:
      - `LegacySellerControllerBase.GetAccessToken()` fallback theo `Authorization header -> Session(ACCESS_TOKEN) -> Claim(ff_access_token/access_token)`.
      - `AdminAccountController.Login` luu them claim `ff_access_token` de phuc hoi session token sau restart/session timeout.
      - Cac Seller controllers da doi sang `GetAccessToken(AccessTokenSessionKey)` thay vi doc session truc tiep, giam loi `HTTP 401` khi session bi mat.
    - Da bo sung seller-scope cho cac API Ordering quan trong:
      - `ReportsAdminController`: scope seller cho toan bo query `Orders` (customers/orders/revenue/products/shipping/review customer mapping).
      - `CouponsAdminController`: scope theo `CreatedBy` (list/detail/create/update/delete/toggle/statistics/validate/send/usage/distribution), tao moi set `CreatedBy` theo seller token.
      - `AdminCustomersController`: scope seller cho `metrics` va `has-orders`.
      - `ShippingAdminController`: scope seller cho list/detail/order-info/create/update/delete/reconcile COD.
      - `ReviewsAdminController`: scope seller cho moderation flow; ownership review map qua `SellerOrderItems -> SellerOrder.SellerId`.
      - `SupportChatAdminController`: scope seller cho conversations/messages/details/close/mark-read theo tap user co order cua seller.
      - `FeedbacksAdminController`: scope seller cho feedback list/detail/update/delete theo contact khach da co order thuoc seller.
    - Da bo sung ownership schema cho Catalog de phuc vu multi-seller:
      - Model: `SellerProduct` + context mapping (`FreshFarmCatalogDBContext.Extras.cs`).
      - DB scripts: `docs/FreshFarmCatalogDb/FreshFarmCatalogDB.2026-03-01.4.6.seller-products.{delta,verify,down}.sql`.
      - Bootstrap script: `docs/FreshFarmCatalogDb/FreshFarmCatalogDB.2026-03-01.4.6.seller-products.seed-template.sql` (gan ownership cho san pham da co).
    - Da bo sung backfill scripts cho ownership o Ordering:
      - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-03-01.4.6.backfill-seller-orders.sql`
      - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-03-01.4.6.backfill-seller-orders.verify.sql`
      - Muc tieu: dien du lieu `SellerOrders/SellerOrderItems` tu `CatalogDB.dbo.SellerProducts` de giam phu thuoc fallback legacy.
    - Da siet fallback tenant-scope theo huong "strict-if-mapped":
      - Neu seller da co ownership mapping (`SellerOrders`/`CreatedBy`) => chi doc du lieu cua seller do.
      - Chi fallback du lieu legacy (order chua map hoac coupon `CreatedBy = NULL`) khi seller chua co mapping ownership.
      - Da apply cho `Orders/Reports/Shipping/Review/SupportChat/Loyalty/AdminCustomers/Coupons`.
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

## Phase 7 - Admin Marketplace Ops (Shopee/Amazon style, theo huong hien co)

### 7.0 Feasibility check (dua tren code + schema hien tai)
- Dashboard tong quan:
  - [~] Co the lam ngay 70% tu `Orders`, `SellerOrders`, `Coupon`, `LoyaltyPointHistory`, `Review`.
  - [!] Traffic realtime (session/visit/concurrent) CHUA co data source chuan trong schema hien tai.
- User management (Buyer/Seller):
  - [~] Co the lam ngay phan user CRUD/co ban role tu `Identity`.
  - [!] Chua co workflow "xet duyet mo shop", "brand registry", "phi hoa hong theo nganh/shop" (thieu schema + API).
- Catalog management + moderation:
  - [~] Co `Categories`, `Products`, `Units`, `SellerProducts` de lam quan ly danh muc/co ban.
  - [!] Chua co bo bang `Attributes/AttributeValues`, queue kiem duyet noi dung, blacklist tu khoa.
- Orders & logistics:
  - [~] Co don hang toan san + shipping records + COD reconcile (nen tang kha dung).
  - [!] Chua co module quan ly "logistics providers" (provider catalog, SLA, API key/webhook manager) o muc enterprise.
- Disputes & CS:
  - [~] Co `SupportConversation/SupportMessage`, `ReturnRequest`, `RefundTransaction` de lam MVP ticket + tranh chap.
  - [!] Chua co workflow arbitration day du (SLA, phan quyen cap bac, evidences pipeline).
- Campaigns:
  - [~] Co `Coupon/CouponDistribution/CouponUsageHistory` de lam voucher do san tai tro.
  - [!] Chua co FlashSale engine va Ads wallet/bidding subsystem.
- Finance, settlement & after-sales:
  - [~] Co the bat dau MVP tu `Orders`, `Payments`, `RefundTransaction`, `ReturnRequest`, `SellerOrders`.
  - [!] Chua co payout cycle, commission ledger, settlement snapshot, chargeback/reconciliation flow o muc san TMĐT.
- Merchant compliance:
  - [~] Co the bat dau tu `Users/Roles` + profile seller hien co.
  - [!] Chua co KYC, ho so phap ly, trang thai duyet shop, chinh sach phi theo seller/category.
- Risk & abuse control:
  - [~] Co the suy ra tin hieu ban dau tu orders/refunds/reviews/coupon usage.
  - [!] Chua co schema risk case, rule engine, decision log, fraud scoring.
- Search/discovery & fresh-goods operations:
  - [!] Chua co search catalog dung nghia marketplace (synonym/tag/facet/ranking).
  - [!] Chua co inventory-by-lot/HSD/truy xuat nguon goc, trong khi domain la nong san/thuc pham.

### 7.1 Nguyen tac UI Admin (giu phong cach Seller)
- [x] Chot dinh huong UI:
  - Dung chung visual language voi Seller: gradient xanh duong, card bo goc, animation "quet anh sang".
  - Khong doi HTML/CSS/JS legacy khi khong can; uu tien wrap va them class/theme trong Admin layout.
- [ ] Tao "Admin theme contract":
  - CSS variables chung (`--ff-primary`, `--ff-accent`, `--ff-gloss`) trong layout Admin.
  - 1 class utility cho hieu ung quet anh sang dung lai tren hero/header cards.
  - checklist responsive desktop/mobile.

### 7.2 Module 1 - Dashboard Tong quan (Analytics & Thong ke)
- Muc tieu:
  - Dashboard dau vao cho Admin, tach khoi Seller scope.
- Buoc nho:
  1. Tao endpoint `Ordering API` moi: `GET /api/orders/platform/dashboard` (`AdminOnly`).
  2. Tinh KPI kinh doanh:
     - GMV, commission revenue, orders moi, cancel rate.
  3. Tinh KPI nguoi dung:
     - users moi, sellers moi (dua tren role + CreatedAt).
  4. Them metric "giao dich dang dien ra" theo cua so 5-15 phut (interim) neu chua co event stream.
  5. Tao BFF `Areas/Admin/Controllers/DashboardController` goi API tren.
  6. Refactor `Areas/Admin/Views/Home/Dashboard.cshtml` theo style Seller + chart blocks.
  7. Them fallback "NO DATA" cho metric chua co traffic telemetry.
- DoD:
  - Dashboard hien KPI + chart, khong leak seller-only scope.

### 7.3 Module 2 - User Management (tach Buyer/Seller ro rang)
- Muc tieu:
  - Admin quan tri user theo nhom role + hanh vi.
- Buoc nho:
  1. Tach lane UI:
     - `Admin/User/ManageAdmins` (giu phan da co).
     - `Admin/User/ManageSellers`.
     - `Admin/User/ManageBuyers`.
  2. Mo rong `Identity API`:
     - endpoint list/filter theo role + trang thai + date range.
  3. Bo sung profile Seller (neu chua co): schema `SellerProfile` + trang thai approve.
  4. Bo sung actions admin:
     - approve/reject/lock/limit visibility cho seller.
  5. Bo sung buyer risk flags:
     - counters don bi huy/hoan/bao cao spam (lay tu Ordering).
  6. Bo sung wallet/member-tier view bridge (neu loyalty dung nhu tier tam thoi).
  7. Cap nhat Admin sidebar + page navigation.
- DoD:
  - Admin xem/sua duoc Buyer va Seller rieng biet, co lock/approve flow co ban.

### 7.4 Module 3 - Catalog Management & Moderation
- Muc tieu:
  - Admin quan ly "nen tang danh muc + kiem duyet noi dung".
- Buoc nho:
  1. Migrate UI danh muc tu Seller sang `Areas/Admin/Catalog`.
  2. Chuyen API categories/units sang lane `AdminOnly` (hoac endpoint admin rieng).
  3. Thiet ke schema attributes:
     - `CategoryAttribute`, `AttributeValueOption`, `ProductAttributeValue`.
  4. Them queue kiem duyet:
     - `ProductModerationQueue` (Pending/Approved/Rejected).
  5. Them blacklist tu khoa/hang cam:
     - `ModerationKeyword` + bo loc pre-publish.
  6. UI moderation list + detail + approve/reject.
  7. Them audit log cho hanh dong kiem duyet.
- DoD:
  - Admin co danh muc trung tam + hang doi duyet san pham.

### 7.5 Module 4 - Orders & Logistics (toan san)
- Muc tieu:
  - Admin theo doi toan bo order flow va hieu suat van chuyen.
- Buoc nho:
  1. Tao endpoint order global (`AdminOnly`) khong seller filter.
  2. Tao BFF `Areas/Admin/Controllers/OrderController` + `ManageOrders` view style Seller.
  3. Mo rong shipping domain:
     - bang `LogisticsProvider` + `ProviderConfig` + `ProviderSlaSnapshot`.
  4. Tao API metrics logistic:
     - success rate, fail rate, on-time rate theo provider.
  5. UI provider management (API key, status, SLA dashboard).
  6. UI order timeline (pending -> processing -> shipping -> completed/cancel/refund).
  7. Bao dam role guard: Admin xem toan san, Seller chi xem phan minh.
- DoD:
  - Admin xem duoc order/logistics toan san, co dashboard theo provider.

### 7.5A Module 5 - Finance, Settlement, Returns & Refunds
- Muc tieu:
  - Bo sung truc song con cua san TMĐT: thu tien, giu tien, chia tien, hoan tien, doi soat, doi tra.
- Buoc nho:
  1. Dinh nghia schema settlement:
     - `PlatformCommissionRule`, `SellerSettlementCycle`, `SellerSettlementLedger`, `SellerPayout`.
  2. Chuan hoa payment/refund ledger:
     - `PaymentTransaction`, `RefundLedger`, `ChargebackCase`, `SettlementSnapshot`.
  3. Dinh nghia workflow after-sales:
     - `ReturnRequest`, `ReturnItem`, `ReturnShipment`, `RefundDecision`.
  4. API admin:
     - approve refund,
     - hold payout,
     - release payout,
     - export reconciliation.
  5. API seller:
     - xem doi soat, payout pending, refund bi tranh chap.
  6. UI Admin finance console:
     - dashboard doanh thu san,
     - cong no seller,
     - refund pipeline,
     - payout queue.
  7. UI seller settlement:
     - chi tiet don duoc doi soat,
     - phi san,
     - lich su payout.
- DoD:
  - Co duoc ledger tai chinh co ban cho san, payout queue MVP, va refund/return flow khong can xu ly tay ngoai he thong.

### 7.6 Module 6 - Disputes & Customer Service
- Muc tieu:
  - Ticket + tranh chap co "trong tai admin".
- Buoc nho:
  1. Dinh nghia schema ticket:
     - `SupportTicket`, `TicketMessage`, `TicketAttachment`, `TicketSla`.
  2. Bridge du lieu tu `SupportConversation` sang ticket lane admin.
  3. Dinh nghia schema tranh chap:
     - `DisputeCase`, `DisputeEvidence`, `DisputeDecision`.
  4. API admin:
     - tao case, assign staff, request evidence, close with decision.
  5. UI CS queue:
     - Open/Pending/SLA breach/Resolved.
  6. UI arbitration center:
     - thong tin buyer/seller/order/return/refund tren 1 man.
  7. Tich hop notifications cho buyer/seller sau khi ra quyet dinh.
- DoD:
  - Co ticketing + dispute flow MVP, co SLA va decision log.

### 7.6A Merchant Lifecycle & Compliance
- Muc tieu:
  - Bien `Seller` tu role ky thuat thanh `merchant` duoc quan ly vong doi day du.
- Buoc nho:
  1. Them `SellerProfile`, `SellerComplianceDocument`, `SellerApprovalHistory`.
  2. Bo sung trang thai merchant:
     - Draft/PendingReview/Approved/Suspended/Closed.
  3. Ho tro tai lieu:
     - CCCD/GPKD/MST/tai khoan ngan hang.
  4. Bo sung chinh sach phi:
     - phi theo nganh hang, shop tier, campaign.
  5. UI Admin:
     - queue duyet mo shop,
     - suspend/reactivate seller,
     - lich su phe duyet.
  6. UI seller:
     - wizard hoan tat ho so,
     - banner compliance missing.
- DoD:
  - Seller onboarding/approval khong con nam ngoai he thong va admin co lich su duyet ro rang.

### 7.7 Module 7 - Campaigns (Flash Sale, Voucher, Ads)
- Muc tieu:
  - Admin van hanh campaign do san tai tro + campaign seller tham gia.
- Buoc nho:
  1. Tach coupon hien tai thanh 2 lane:
     - voucher cua san (AdminOnly),
     - voucher cua seller (SellerOnly).
  2. Tao schema campaign:
     - `Campaign`, `CampaignSellerParticipation`, `CampaignProductSlot`.
  3. Tao flow flash sale:
     - tao campaign, mo dang ky, duyet seller tham gia.
  4. Tao KPI campaign:
     - GMV, conversion, ROI voucher.
  5. Tao schema ads wallet:
     - `SellerAdsWallet`, `AdsTopup`, `AdsSpendLedger`, `AdsCampaign`.
  6. UI Admin campaign center + ads finance overview.
  7. Export/report cho doi marketing.
- DoD:
  - Admin van hanh duoc voucher + campaign flash sale MVP; ads wallet co ledger co ban.

### 7.7A Cross-cutting Marketplace Controls
- Muc tieu:
  - Bo sung cac truc can co de san van hanh ben vung, khong bi dung o muc "CRUD co dep UI".
- Buoc nho:
  1. Risk/fraud center:
     - `RiskCase`, `RiskSignal`, `RiskDecision`, `VoucherAbuseCase`.
  2. Audit log:
     - `AdminActionLog`, `ModerationAudit`, `SettlementAudit`.
  3. Search/catalog readiness:
     - `CategoryAttribute`, `ProductAttributeValue`, synonym/tag/search facet.
  4. Fresh-goods operations:
     - lot/HSD/FIFO-FEFO/truy xuat nguon goc/quality issue recall.
  5. Communications governance:
     - template thong bao/email/SMS,
     - notification policy theo event,
     - opt-in/out tracking.
- DoD:
  - Co he thong audit + risk + catalog quality + domain operations du de goi la san TMĐT van hanh that.

### 7.8 Thu tu trien khai de xong nhanh
- Sprint A (uu tien cao):
  1. Dashboard tong quan.
  2. User management tach Buyer/Seller.
  3. Orders global + logistics base.
- Sprint B:
  1. Catalog moderation.
  2. Finance/Settlement + Returns/Refunds.
  3. Disputes & CS.
- Sprint C:
  1. Merchant lifecycle & compliance.
  2. Campaigns (voucher san + flash sale).
  3. Ads wallet.
- Sprint D:
  1. Risk/fraud center.
  2. Search/catalog readiness + fresh-goods operations.
  3. Audit + communications governance.

### 7.9 Definition of Ready / Done cho moi module Admin
- Ready:
  - Co API contract (request/response), role policy, data source ro rang.
  - Co mapping UI state (loading/empty/error/success).
- Done:
  - Co route BFF + view + API endpoint.
  - Co role guard `AdminOnly`.
  - Co smoke test tay (desktop + mobile).
  - Khong tac dong regress lane Seller.

### 7.10 Nguyen tac giu dung huong san TMĐT
- Uu tien module theo thu tu:
  1. Runtime on dinh + tenant isolation.
  2. Order/logistics/refund/settlement.
  3. Merchant compliance + catalog quality.
  4. Disputes/risk/audit.
  5. Campaigns/ads/growth.
- Khi can chon giua `UI dep hon` va `ledger/nghiep vu dung hon`:
  - uu tien ledger/nghiep vu.
- Khi can chon giua `feature growth` va `finance/compliance`:
  - uu tien finance/compliance.
- Khi can chon giua `module phu tro van hanh` va `core marketplace flow`:
  - uu tien core marketplace flow.
