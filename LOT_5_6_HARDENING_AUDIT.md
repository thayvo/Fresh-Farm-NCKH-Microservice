# Lot 5.6 - Hardening + Auth + Static Audit (cum 5)

## 1) Scope
- Cum 5 controllers:
  - `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/{SettingController,UserController,SupportChatController,ReviewController,ReportController}.cs`
- Cum 5 views:
  - `Areas/Seller/Views/Setting/Index.cshtml`
  - `Areas/Seller/Views/User/ManageUsers.cshtml`
  - `Areas/Seller/Views/SupportChat/Index.cshtml`
  - `Areas/Seller/Views/Review/{ManageReview,ReportedReviews}.cshtml`
  - `Areas/Seller/Views/Report/{Customer,Order,Revenue,Product,Shipping,Review}.cshtml`
- Related service APIs (auth hardening check):
  - Catalog: `Products/Categories/Units` admin write endpoints
  - Ordering: admin order/support-chat/review/report endpoints
  - Identity: admin user/setting/customer endpoints

## 2) Auth hardening ket qua
- BFF Seller auth:
  - Da xac nhan cum 5 dung `[Authorize(Roles = "Seller")]`.
  - Da siet them role Seller cho cac controller Seller con dung `[Authorize]` chung:
    - `DeliveryController`
    - `FeedbackController`
    - `HomeController`
    - `LoyaltyController`
    - `StatusController`
  - Ngoai le hop ly:
    - `AdminAccountController` van giu `[Authorize]` class-level + `[AllowAnonymous]` cho `Login`.
- Service policy auth:
  - Da xac nhan `SellerOnly` cho endpoints lien quan o:
    - `Catalog API` (`Products/Categories/Units` write)
    - `Ordering API` (`Orders` admin routes, `SupportChatAdmin`, `ReviewsAdmin`, `ReportsAdmin`)
    - `Identity API` (`AdminUsers`, `AdminSettings`, `AdminCustomers`)

## 3) Static audit ket qua
- Command cum 5:
  - `rg -n "FreshFram|FreshFarmDBEntities|System.Data.Entity|AdminAuthorize|NoCache" <controllers+views cum 5>`
- Ket qua cum 5:
  - `0` match (cum 5 sach legacy dependency pattern).

- Command toan bo Seller area:
  - `rg -n "FreshFram|FreshFarmDBEntities|System.Data.Entity|AdminAuthorize|NoCache" src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers src/Web/FreshFarm.Web.Bff/Areas/Seller/Views`
- Ket qua toan bo Seller area:
  - Update 2026-02-28: `0` match sau khi complete Phase 5 va clean-up artifacts legacy views.

## 4) Smoke checklist routes (de test tay)
- Cum 5 pages:
  - `GET /Seller/Setting/Index`
  - `GET /Seller/User/ManageUsers`
  - `GET /Seller/SupportChat/Index`
  - `GET /Seller/Review/ManageReview`
  - `GET /Seller/Review/ReportedReviews`
  - `GET /Seller/Report/Customer`
  - `GET /Seller/Report/Order`
  - `GET /Seller/Report/Revenue`
  - `GET /Seller/Report/Product`
  - `GET /Seller/Report/Shipping`
  - `GET /Seller/Report/Review`
- Cum 5 JSON/actions:
  - `GET /Seller/SupportChat/Conversations`
  - `GET /Seller/SupportChat/Messages?conversationId=...`
  - `POST /Seller/SupportChat/MarkAsRead`
  - `POST /Seller/SupportChat/Close`
  - `GET /Seller/Review/GetReviewReports?reviewId=...&page=1`
  - `POST /Seller/Review/ResolveReport`
  - `POST /Seller/Review/ToggleVisibility`
  - `POST /Seller/Review/ReplyReview`
  - `POST /Seller/Report/DeleteReview`
- Export (service-first proxy):
  - `GET /Seller/Report/ExportCustomerExcel`
  - `GET /Seller/Report/ExportOrderExcel`
  - `GET /Seller/Report/ExportRevenueExcel`
  - `GET /Seller/Report/ExportProductExcel`
  - `GET /Seller/Report/ExportShippingExcel`
  - `GET /Seller/Report/ExportReviewExcel`

## 5) Open points sau lot 5.6
- Legacy dependency pattern trong Seller area da duoc don sach (static audit = 0).
- System-level gaps (ngoai scope lot 5 can xu ly truoc phase moi):
  - DA XU LY lot 4.2: compile gate Seller da chuyen sang gate chon loc module legacy, khong con tat toan bo `Areas/Seller/**`.
  - DA XU LY lot 4.3: `_LayoutAdmin.cshtml` da bo references script khong ton tai (`~/Scripts/jquery-3.7.0.min.js`, `~/Scripts/jquery.signalR-2.4.3.min.js`, `~/signalr/hubs`).
  - DA XU LY lot 4.4 (muc fallback): `support-chat-admin.js` co polling-safe guard khi khong co SignalR hub; realtime hub mode day du van chua bat.
- UNCONFIRMED runtime: environment hien tai khong co `dotnet`, chua build/chay duoc de xac nhan runtime e2e.
