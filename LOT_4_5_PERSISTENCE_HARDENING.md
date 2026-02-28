# Lot 4.5 - Persistence hardening cho endpoints tam

## Muc tieu
- Loai bo state business bi mat khi restart service.
- Giu contract API hien tai, uu tien thay doi nho va an toan.

## 4.5.1 - Da hoan tat (file-backed persistence truoc, khong doi schema DB)
- `Identity API`:
  - `src/Services/Identity/FreshFarm.Identity.API/Controllers/AdminSettingsController.cs`
    - `AdminStoreSettingsResponse` khong con chi luu static RAM.
    - Them persisted file:
      - `App_Data/identity.admin-store-settings.json`
  - `src/Services/Identity/FreshFarm.Identity.API/Controllers/AdminUsersController.cs`
    - Avatar map khong con chi luu static RAM.
    - Them persisted file:
      - `App_Data/identity.admin-users.avatars.json`

- `Ordering API`:
  - `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ReportsAdminController.cs`
    - `DeletedReviewIds` khong con chi luu RAM.
    - Them persisted file:
      - `App_Data/ordering.reports.deleted-review-ids.json`

## 4.5.2 - Da hoan tat
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/SupportChatAdminController.cs`
  - `ConversationStore`, `MessageStore` da co file-backed persistence.
  - Them persisted file:
    - `App_Data/ordering.support-chat.store.json`
  - Da co load-once + save-on-write cho cac luong close/mark-read/seed.

## 4.5.3 - Da hoan tat
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ReviewsAdminController.cs`
  - `ReviewStore`, `ReportStore` da co file-backed persistence.
  - Them persisted file:
    - `App_Data/ordering.reviews.store.json`
  - Da co load-once + save-on-write cho cac action write:
    - resolve/delete/approve/toggle/reply/update.

## 4.5.4 - Da execute migration schema + verify tren DB that
- Da tao plan chi tiet:
  - `docs/LOT_4_5_4_DB_SCHEMA_MIGRATION_ROLLBACK_PLAN.md`
- Da tao SQL drafts:
  - `docs/lot-4.5.4-ordering-up.sql`
  - `docs/lot-4.5.4-ordering-down.sql`
  - `docs/lot-4.5.4-identity-up.sql`
  - `docs/lot-4.5.4-identity-down.sql`
  - `docs/lot-4.5.4-sql-complete.md`
  - Service DB deltas (de dong bo voi 3 DB scripts goc):
    - `docs/FreshFarmIdentityDb/FreshFarmIdentityDb.2026-02-27.4.5.4.delta.sql`
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-27.4.5.4.delta.sql`
  - Hotfix index Ordering (sau verify fail lan 1):
    - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.4.5.4.hotfix-indexes.sql`
- Mapping schema dua tren `docs/final3.sql`:
  - Ordering: `ContactMessages`, `SupportConversations`, `SupportMessages`, `SupportMessageReactions`, `Review`, `ReviewReport`.
  - Identity: `StoreSettings` + `Users.Avatar`.
- Trang thai:
  - Hoan tat migration schema tren DB that va verify pass.
  - Identity verify: pass (`StoreSettings`, `Users.Avatar`).
  - Ordering verify: pass sau khi bo sung hotfix index `IX_ContactMessages_Status` va `IX_ContactMessages_IsDeleted_CreatedAt`.

## 4.5.5 - Cutover controllers sang DB-backed (code-level)
- Da chuyen cac controller sau sang EF Core DB-backed (khong con doc/ghi `App_Data/*.json`):
  - `src/Services/Identity/FreshFarm.Identity.API/Controllers/AdminSettingsController.cs`
    - Doc/ghi truc tiep bang `StoreSettings`.
  - `src/Services/Identity/FreshFarm.Identity.API/Controllers/AdminUsersController.cs`
    - Dung cot `Users.Avatar` truc tiep.
  - `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/SupportChatAdminController.cs`
    - Dung bang `SupportConversations`, `SupportMessages`, `SupportMessageReactions`.
  - `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/FeedbacksAdminController.cs`
    - Dung bang `ContactMessages` (list/update-status/delete soft-delete).
  - `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ReviewsAdminController.cs`
    - Dung bang `Review`, `ReviewReport` (co soft-delete).
  - `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ReportsAdminController.cs` (review path)
    - Dung `Review` thay cho deleted-id JSON marker.
- Luu y:
  - Chua build/runtime verify duoc trong moi truong nay vi thieu `dotnet`.
  - Can execute SQL delta truoc khi deploy code cutover.

## 4.5.6 - Backfill script san sang cho staging
- Da them script backfill tu JSON store cu -> DB:
  - `docs/FreshFarmIdentityDb/FreshFarmIdentityDb.2026-02-27.4.5.4.backfill.sql`
  - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-27.4.5.4.backfill.sql`
- Muc tieu:
  - Ho tro migrate du lieu tam thoi trong `App_Data/*.json` (neu co) sang schema moi.

## 4.5.7 - Runtime triage + final3 customer migration plan
- Da bo sung script triage runtime de check du lieu that cho Seller pages:
  - `docs/FreshFarmOrderingDb/FreshFarmOrderingDB.2026-02-28.seller-runtime-diagnostics.sql`
  - Scope:
    - Nguon du lieu `ManageOrders`.
    - Doanh thu/so don theo status.
    - Du lieu dau vao cho `Report/Product`.
- Da bo sung plan migrate du lieu khach hang tu monolith `final3.sql`:
  - `docs/LOT_4_5_6_FINAL3_CUSTOMER_MIGRATION_2026-02-28.md`
  - Gom:
    - Mapping `Users/AddressBook/Orders/OrderDetail`.
    - Chuan hoa status `Completed -> Delivered`, `Cancelled -> Canceled`.
    - Trinh tu backup -> stage -> upsert -> verify -> smoke.

## Cac lenh nho de ra tiep (micro-commands)
1. `Lam lot 4.5.6: migrate customer data tu final3.sql theo plan + verify runtime`
2. `Lam lot 4.6: re-check 404/binding toan Seller views sau khi copy tu legacy`
3. `Lam static audit + smoke test toan Seller area de chot Phase 5 clean`

## Luu y van hanh
- File store la buoc trung gian de dat muc "restart khong mat state" nhanh.
- Khong thay cho DB persistence dai han.
