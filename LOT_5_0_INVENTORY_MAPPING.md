# Lot 5.0 - Inventory + Mapping (khong sua logic)

## 1) Scope da inventory
- Seller controllers:
  - `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/SettingController.cs`
  - `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/UserController.cs`
  - `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/SupportChatController.cs`
  - `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/ReviewController.cs`
  - `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/ReportController.cs`
- Seller views:
  - `Areas/Seller/Views/Setting/Index.cshtml`
  - `Areas/Seller/Views/User/ManageUsers.cshtml`
  - `Areas/Seller/Views/SupportChat/Index.cshtml`
  - `Areas/Seller/Views/Review/ManageReview.cshtml`
  - `Areas/Seller/Views/Review/ReportedReviews.cshtml`
  - `Areas/Seller/Views/Report/{Customer,Order,Revenue,Product,Shipping,Review}.cshtml`

## 2) Static audit ket qua (legacy con ton tai)
- Command:
  - `rg -n "FreshFram|FreshFarmDBEntities|System.Data.Entity" <controllers+views cum 5>`
- Ket qua:
  - Con `FreshFram.*`, `FreshFarmDBEntities`, `System.Data.Entity` trong toan bo 5 controllers.
  - Con `FreshFram.*` o toan bo views thuoc cum 5 (model namespace).
  - `SupportChat/Index.cshtml` tham chieu `~/Scripts/support-chat-admin.js`; file da duoc copy ve:
    - `src/Web/FreshFarm.Web.Bff/wwwroot/Scripts/support-chat-admin.js`.

## 3) Action inventory + action-to-API mapping de xuat

Quy uoc endpoint de xuat:
- `Identity API` cho admin user management.
- `SellerOps API` (service moi/aggregator) cho Setting, SupportChat, Review moderation, Report read/export.
- Muc nay la mapping de migrate, chua phai implementation.
- Ghi chu cap nhat:
  - Lot 5.2 da implement SupportChat theo `Ordering API` route `api/orders/admin/support-chat/*` de giu service-first va contract view hien tai.
  - Lot 5.3 da implement Review theo `Ordering API` route `api/orders/admin/reviews/*`.
  - Lot 5.4 da implement Report read theo `Ordering API` route `api/orders/admin/reports/*`.
  - Lot 5.5 da implement Report export theo `Ordering API` route `api/orders/admin/reports/*/export` (CSV mo bang Excel).

### 3.1 SettingController
| Action (line) | HTTP | Output hien tai | Nguon hien tai | API endpoint de xuat |
|---|---|---|---|---|
| `Index` (18) | GET | View `Setting/Index` | `db.Settings.FirstOrDefault()` + tao default | `GET /api/seller-ops/settings/store` |
| `Save` (61) | POST | JSON `{success,message}` | upsert `db.Settings` | `PUT /api/seller-ops/settings/store` |

### 3.2 UserController
| Action (line) | HTTP | Output hien tai | Nguon hien tai | API endpoint de xuat |
|---|---|---|---|---|
| `ManageUsers` (27) | GET | View `User/ManageUsers` | `db.UserAdmins.Include("Role")` | `GET /api/admin-users?sort=created_desc` |
| `SearchUsers` (39) | POST | View `ManageUsers` | filter local theo `userName/fullName/email/phone` | `GET /api/admin-users?search={q}` |
| `GetUserById` (61) | GET | JSON user detail | `db.UserAdmins` | `GET /api/admin-users/{id}` |
| `CreateUser` (91) | POST | JSON `{success,message}` | validate + insert `db.UserAdmins` | `POST /api/admin-users` |
| `UpdateUser` (149) | POST | JSON `{success,message}` | update `db.UserAdmins` | `PUT /api/admin-users/{id}` |
| `DeleteUser` (203) | POST | JSON `{success,message}` | delete `db.UserAdmins` | `DELETE /api/admin-users/{id}` |

### 3.3 SupportChatController
| Action (line) | HTTP | Output hien tai | Nguon hien tai | API endpoint de xuat |
|---|---|---|---|---|
| `Index` (24) | GET | View `SupportChat/Index` | render page | `GET /seller/support-chat` (BFF page) |
| `Conversations` (30) | GET | JSON `{ok,conversations}` | `_chatService.GetConversationsForAdmin()` | `GET /api/orders/admin/support-chat/conversations` |
| `Messages` (37) | GET | JSON `{ok,messages}` | `_chatService.GetMessages(conversationId,take)` | `GET /api/orders/admin/support-chat/conversations/{id}/messages?take=` |
| `ConversationDetails` (44) | GET | JSON `{ok,profile,orders}` | `db.SupportConversations` + `db.Orders` | `GET /api/orders/admin/support-chat/conversations/{id}/details` |
| `Close` (98) | POST | JSON `{ok:true}` | `_chatService.CloseConversation` | `POST /api/orders/admin/support-chat/conversations/{id}/close` |
| `MarkAsRead` (105) | POST | JSON `{ok:true}` | `_chatService.MarkAsRead(...,true)` | `POST /api/orders/admin/support-chat/conversations/{id}/mark-read` |

### 3.4 ReviewController
| Action (line) | HTTP | Output hien tai | Nguon hien tai | API endpoint de xuat |
|---|---|---|---|---|
| `ManageReview` (19) | GET | View `Review/ManageReview` | `db.Reviews.Include(Product,User)` | `GET /api/seller-ops/reviews?includeReplies=true` |
| `FilterReviews` (32) | GET | View `ManageReview` | filter `search/rating/status` | `GET /api/seller-ops/reviews?search=&rating=&status=` |
| `ReportedReviews` (81) | GET | View `Review/ReportedReviews` | group `db.ReviewReports(Status=0)` | `GET /api/seller-ops/reviews/reported` |
| `ResolveReport` (125) | POST | JSON | cap nhat `ReviewReports` + review status/delete | `POST /api/seller-ops/reviews/{id}/reports/resolve` |
| `DeleteReview` (172) | POST | JSON | remove review + replies | `DELETE /api/seller-ops/reviews/{id}` |
| `ApproveReview` (194) | POST | JSON | `IsApproved=true` | `POST /api/seller-ops/reviews/{id}/approve` |
| `ToggleVisibility` (208) | POST | JSON `{approved,...}` | toggle `IsApproved` | `POST /api/seller-ops/reviews/{id}/visibility/toggle` |
| `ReplyReview` (227) | POST | JSON | tao review reply + mirror admin user | `POST /api/seller-ops/reviews/{id}/replies` |
| `UpdateReview` (278) | POST | JSON | update `comment/isApproved` | `PATCH /api/seller-ops/reviews/{id}` |
| `GetReviewReports` (297) | GET | JSON paged reports | `db.ReviewReports` | `GET /api/seller-ops/reviews/{id}/reports?page=` |

### 3.5 ReportController
| Action (line) | HTTP | Output hien tai | Nguon hien tai | API endpoint de xuat |
|---|---|---|---|---|
| `Customer` (27) | GET | View `Report/Customer` | `Users + Orders + Payments` | `GET /api/seller-ops/reports/customers` |
| `ExportCustomerExcel` (145) | GET | File `.xlsx` | aggregate + EPPlus | `GET /api/seller-ops/reports/customers/export` |
| `Order` (503) | GET | View `Report/Order` | `Orders + Payments + Users` | `GET /api/seller-ops/reports/orders` |
| `ExportOrderExcel` (679) | GET | File `.xlsx` | aggregate + EPPlus | `GET /api/seller-ops/reports/orders/export` |
| `Revenue` (1009) | GET | View `Report/Revenue` | `Orders + Payments + OrderDetails + Products` | `GET /api/seller-ops/reports/revenue` |
| `ExportRevenueExcel` (1093) | GET | File `.xlsx` | aggregate + EPPlus | `GET /api/seller-ops/reports/revenue/export` |
| `Product` (1435) | GET | View `Report/Product` | `Orders + Payments + OrderDetails + Products + Categories` | `GET /api/seller-ops/reports/products` |
| `ExportProductExcel` (1615) | GET | File `.xlsx` | aggregate + EPPlus | `GET /api/seller-ops/reports/products/export` |
| `Shipping` (1914) | GET | View `Report/Shipping` | `Orders + Shippings + DeliveryAssignments + UserAdmins` | `GET /api/seller-ops/reports/shipping` |
| `ExportShippingExcel` (1979) | GET | File `.xlsx` | aggregate + EPPlus | `GET /api/seller-ops/reports/shipping/export` |
| `Review` (2639) | GET | View `Report/Review` | `Reviews + User + Product` | `GET /api/seller-ops/reports/reviews` |
| `ExportReviewExcel` (2788) | GET | File `.xlsx` | aggregate + EPPlus | `GET /api/seller-ops/reports/reviews/export` |
| `DeleteReview` (3113) | POST | JSON | xoa `Review + ReviewReports` | `DELETE /api/seller-ops/reports/reviews/{id}` |

## 4) View-model field mapping toi thieu (de giu compatibility)

### 4.1 Setting
- File: `Areas/Seller/Views/Setting/Index.cshtml`
- `@model Setting` fields toi thieu:
  - `StoreName`, `StoreAddress`, `StoreEmail`, `StorePhone`
  - `IsCODEnabled`, `BankTransferInstructions`, `BankAccountInfo`
  - `DefaultShippingFee`, `FreeShippingThreshold`
  - `IsEmailNewOrderEnabled`, `IsEmailDeliveredEnabled`, `IsEmailCancelledEnabled`
  - `AdminNotificationEmail`
- JSON response cho submit:
  - `success` (bool), `message` (string)

### 4.2 User
- File: `Areas/Seller/Views/User/ManageUsers.cshtml`
- `@model IEnumerable<UserAdmin>` fields toi thieu tren row:
  - `AdminID`, `UserName`, `FullName`, `Email`, `Phone`, `Avatar`, `IsActive`
  - nested `Role.RoleID`, `Role.RoleName`
- `ViewBag.Roles`: `IEnumerable<Role>` voi `RoleID`, `RoleName`.
- JSON contract:
  - `GetUserById`: `data.userId,userName,fullName,email,phone,avatar,roleId,isActive,lastLogin,created,updated`
  - `Create/Update/Delete`: `success,message`

### 4.3 SupportChat
- File: `Areas/Seller/Views/SupportChat/Index.cshtml`
- View khong strongly-typed, consume JSON tu:
  - `Conversations`, `Messages`, `ConversationDetails`, `Close`, `MarkAsRead`.
- Contract chac chan tu controller:
  - `ConversationDetails`:
    - `profile.userId,fullName,email,phone,avatarUrl,totalPoints,rankName`
    - `orders[].orderId,orderCode,orderDate,totalAmount,status`
  - `Close/MarkAsRead`: `{ok:true}`
- Ghi chu:
  - File script da co trong workspace: `wwwroot/Scripts/support-chat-admin.js`.

### 4.4 Review (moderation)
- File: `Areas/Seller/Views/Review/ManageReview.cshtml`
- `@model List<Review>` fields toi thieu:
  - review goc: `ReviewID,ReplyTo,UserID,ProductID,Rating,Comment,CreatedAt,IsApproved`
  - nested: `User.FullName,User.UserName`, `Product.ProductName,Product.ImageFileName`
  - reports: `ReviewReports[].Status` (de dem report mo)
  - replies: dung lai cung type `Review`.
- JSON contract:
  - `ApproveReview/DeleteReview/ReplyReview/ToggleVisibility`: `success,message` (+ `approved` o toggle)
  - `GetReviewReports`: `success,data[],page,totalPages`, item `reporter,reason,note,createdAt`.

- File: `Areas/Seller/Views/Review/ReportedReviews.cshtml`
- `@model IEnumerable<ReportedReviewRow>` fields toi thieu:
  - `ReviewID,CustomerName,ProductName,Rating,OpenCount,FirstReportAt,Comment`

### 4.5 Report
- File: `Report/Customer.cshtml`
  - model fields:
    - filter: `RegisterFromDate,RegisterToDate,CustomerSegment`
    - stats: `TotalCustomers,NewCustomers,ReturnRateFormatted,AverageSpendingFormatted`
    - chart: `GrowthLabels,GrowthData`
    - segment: `NewCustomerCount,ReturningCustomerCount,VIPCustomerCount,OtherCustomerCount`
    - top list: `TopCustomers[].Rank,UserID,AvatarUrl,FullName,CreatedDateFormatted,TotalOrders,TotalSpentFormatted`

- File: `Report/Order.cshtml`
  - model fields:
    - filter: `FromDate,ToDate,OrderStatus`
    - stats: `TotalOrders,SuccessOrders,CancelledOrders,CancelRate`
    - chart: `TrendLabels,TrendTotalData,TrendSuccessData,TrendCancelledData`
    - status pie: `StatusSuccessCount,StatusShippingCount,StatusPendingCount,StatusCancelledCount`
    - recent: `RecentOrders[].OrderID,OrderCode,CustomerName,OrderDateFormatted,TotalAmount,StatusBadgeClass,StatusText`

- File: `Report/Revenue.cshtml`
  - model fields:
    - filter: `FromDate,ToDate,ViewBy`
    - stats: `TotalRevenue,TotalOrders,AverageOrderValue,EstimatedProfit`
    - chart: `TrendLabels,TrendData`
    - top chart: `TopProducts[].ProductName,TotalRevenue`
    - table: `DailyRevenues[].DateFormatted,TotalOrders,TotalProducts,Revenue,Profit`

- File: `Report/Product.cshtml`
  - model fields:
    - filter: `FromDate,ToDate,ProductCategory`
    - stats: `BestSellingProduct,TotalProductsSold,AverageRevenuePerProduct,LowStockProducts`
    - chart: `TopSellingProducts[].ProductName,QuantitySold`
    - chart: `CategoryRevenues[].CategoryName,Revenue`
    - table: `ProductPerformances[].ImageFileName,ProductName,Sku,CategoryName,QuantitySold,StockBadgeClass,StockStatus,TotalRevenue`
    - paging: `CurrentPage,TotalPages`

- File: `Report/Shipping.cshtml`
  - model fields:
    - filter: `FromDate,ToDate,SelectedStaffId,Status,Query,Sort,CurrentPage,PageSize`
    - stats: `DeliveredOrders,TotalShippingFee,AverageDeliveryTime,ReturnRate`
    - chart: `StaffPerformances[].StaffName,TotalOrders,SuccessRate`
    - chart: `DeliveryTimeDistributions[].TimeRange,OrderCount`
    - table: `RecentShippings[].OrderCode,CustomerName,CustomerPhone,DeliveryStaffName,DeliveryAddress,ShippingDateFormatted,ExpectedDeliveryDateFormatted,StatusBadgeClass,StatusText,ActualDeliveryDays`
    - paging: `TotalPages,TotalRecords`
  - ViewBag:
    - `DeliveryStaffs` (`SelectListItem.Value/Text`)

- File: `Report/Review.cshtml`
  - model fields:
    - filter: `FromDate,ToDate,StarRating`
    - stats: `TotalReviews,AverageRating,PositiveReviews,PositivePercentage,NegativeReviews,NegativePercentage`
    - chart: `StarDistributions[].Label,Count`
    - topics: `TopMentionedTopics[]`
    - table: `RecentReviews[].ReviewID,UserID,CustomerName,ProductName,ProductImageFileName,StarDisplay,Comment,CreatedAtFormatted`
  - paging:
    - `ViewBag.Page,ViewBag.PageSize,ViewBag.TotalPages,ViewBag.Total`
  - delete contract:
    - `POST DeleteReview` tra `success,message`.

## 5) Open gaps cho lot tiep theo
- Cum 5 da migrate xong theo service-first (5.1 -> 5.6), xem `ROADMAP_SELLER_ADMIN_MIGRATION.md`.
- Van con overlap nghiep vu xoa review o `ReviewController.DeleteReview` va `ReportController.DeleteReview` (2 route khac nhau theo 2 luong du lieu).
- `SupportChat` da duoc chot polling-safe fallback (khong vo JS neu khong co SignalR hub), nhung realtime hub mode (send/typing/reaction realtime) chua bat.
- Cap nhat 2026-02-27: Phase 5 module `Loyalty` va `Status` da migrate service-first, static audit Seller area khong con `FreshFram.*`, `FreshFarmDBEntities`, `System.Data.Entity`.
