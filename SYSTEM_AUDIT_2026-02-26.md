# System Audit - 2026-02-26

## 1) Scope audit
- Scan toan bo `src` cho:
  - legacy dependency (`FreshFram.*`, `FreshFarmDBEntities`, `System.Data.Entity`, `AdminAuthorize`, `NoCache`)
  - auth consistency (`SellerOnly`, `[Authorize(Roles = "Seller")]`)
  - runtime blocking gaps (compile gate, static assets, config runtime)
  - docs/runtime mismatch.

## 2) Ket qua tong hop

### High priority gaps (can xu ly truoc)
1. Seller compile gate dang tat toan bo module Seller mac dinh:
   - File: `src/Web/FreshFarm.Web.Bff/FreshFarm.Web.Bff.csproj`
   - Dang co:
     - `<EnableLegacySeller>false</EnableLegacySeller>`
     - `<Compile Remove="Areas/Seller/**/*.cs" />`
     - remove toan bo Seller `.cshtml`.
   - Tac dong:
     - Cac module Seller da migrate khong duoc compile/run trong default build.
   - Cap nhat:
     - DA XU LY trong lot 4.2: da doi sang gate chon loc module legacy chua migrate.
     - Con lai: can verify build/runtime khi co `dotnet`.

2. Layout admin dang goi script khong ton tai:
   - File: `src/Web/FreshFarm.Web.Bff/Areas/Seller/Views/Shared/_LayoutAdmin.cshtml`
   - Dang tham chieu:
     - `~/Scripts/jquery-3.7.0.min.js`
     - `~/Scripts/jquery.signalR-2.4.3.min.js`
     - `~/signalr/hubs`
   - Workspace hien tai:
     - `src/Web/FreshFarm.Web.Bff/wwwroot/Scripts/` chi co `support-chat-admin.js`.
   - Tac dong:
     - Seller JS de vo ngay tu layout (404 + missing globals).
   - Cap nhat:
     - DA XU LY trong lot 4.3:
       - Chuyen sang `~/lib/jquery/dist/jquery.min.js`.
       - Bo references `~/Scripts/jquery.signalR-2.4.3.min.js` va `~/signalr/hubs`.

3. SupportChat script con phu thuoc legacy SignalR:
   - File: `src/Web/FreshFarm.Web.Bff/wwwroot/Scripts/support-chat-admin.js`
   - Dang goi: `$.connection.supportChatHub` va `$.connection.hub.start()`.
   - Hien tai chua co hub mapping trong BFF/API theo ASP.NET Core SignalR.
   - Tac dong:
     - SupportChat co nguy co runtime error, can chot polling-only hoac implement hub core.
   - Cap nhat:
     - DA GIAM RUI RO trong lot 4.4:
       - Them guard/fallback polling-safe khi khong co SignalR hub.
       - Khong con vo JS do `$.connection` undefined.
     - Con lai:
       - Chua co realtime hub mode day du (send/typing/reaction qua hub).

### Medium priority gaps
1. Legacy con ton tai o cum Seller ngoai scope lot 5:
   - Controllers/views lien quan:
     - `AdminAccount`, `Home`, `Feedback`, `Delivery`, `Loyalty`, `Status`.
   - Muc lot 5 da xong, nhung chua xong toan Seller area.

2. Config/doc chua dong bo het:
   - `appsettings.Development.json.example` cua BFF chua phan anh section `Services:*`.
   - Ordering thieu `appsettings.Development.json.example`.
   - `src/README.md` con cac line mang tinh khoi tao ban dau, chua phan anh day du status migrate hien tai.

3. In-memory state con ton tai tren API admin:
   - Ordering:
     - `SupportChatAdminController` (`ConversationStore`, `MessageStore`)
     - `ReviewsAdminController` (`ReviewStore`, `ReportStore`)
     - `ReportsAdminController` (`DeletedReviewIds`)
   - Identity:
     - `AdminSettingsController` (`_storeSettings`)
     - `AdminUsersController` (`AvatarByUserId`)
   - Can xem day la transition hay final behavior.

## 3) Di dung huong hay sai huong?
- Ket luan:
  - Huong `service-first` la dung.
  - Van de hien tai khong phai "sai kien truc", ma la "thieu lop on dinh runtime" truoc khi mo phase moi.
- Hanh dong de xac lap lai huong:
  - Uu tien System Stabilization (Phase 4 moi trong roadmap da cap nhat).
  - Sau do moi tiep tuc go cum legacy ngoai lot 5 va mo rong Admin.

## 4) Constraints khi audit
- `dotnet` khong co trong environment assistant (`dotnet: command not found`).
- UNCONFIRMED runtime e2e/build chinh thuc cho toan solution.

## 5) Command inventory da dung
- `rg -n "FreshFram|FreshFarmDBEntities|System.Data.Entity|AdminAuthorize|NoCache" src`
- `rg -n "SellerOnly|Authorize\\(Roles = \\\"Seller\\\"\\)" ...`
- `rg -n "~/Scripts/|/signalr/hubs" src/Web/FreshFarm.Web.Bff/Areas/Seller/Views`
- `sed -n ...` de doi chieu `Program.cs`, controllers, appsettings, roadmap.
