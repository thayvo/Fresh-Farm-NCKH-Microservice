# Lộ Trình Đọc Recommendation Trong Fresh Farm

Tài liệu này giúp bạn đọc bộ code recommendation trong `HocGoiY` theo đúng thứ tự dễ hiểu nhất.

Mục tiêu:

- Hiểu recommendation system trong dự án này được viết bằng gì.
- Biết nên đọc file nào trước, file nào sau.
- Hiểu mối liên hệ giữa lý thuyết và code thực tế.
- Không bị “ngợp” khi mở ngay file rất dài như `BffCatalogController.cs`.

## 1. Recommendation trong dự án này viết bằng gì?

Phần recommendation hiện tại được viết chủ yếu bằng:

- `C#` trong ASP.NET Core:
  - BFF làm nhiệm vụ xếp hạng, ghép tín hiệu, chia section, gắn reason/tag.
  - Ordering làm nhiệm vụ lưu tín hiệu, materialize score, trả dữ liệu cá nhân hóa.
- `PowerShell`:
  - Dùng để chạy script đánh giá nhanh output recommendation ngoài runtime.

Nói ngắn gọn:

- `Ordering` là nơi “nuôi dữ liệu sở thích”.
- `BFF` là nơi “ra quyết định hiển thị gì cho người dùng”.

## 2. Bạn nên đọc lý thuyết trước hay đọc code trước?

Mình khuyên:

1. Đọc lý thuyết thật ngắn để có khung đầu.
2. Đọc code BFF để thấy cách xếp hạng thực tế.
3. Quay lại lý thuyết lần 2 để đối chiếu.
4. Đọc Ordering để hiểu dữ liệu cá nhân hóa được sinh ra từ đâu.

## 3. Thứ tự đọc lý thuyết

1. [01_Tổng_Quan_Hệ_Thống_Gợi_Ý.md](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/LyThuyet/01_Tong_Quan_He_Thong_Goi_Y.md)
2. [02_Content_Based_Filtering.md](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/LyThuyet/02_Content_Based_Filtering.md)
3. [03_Collaborative_Filtering.md](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/LyThuyet/03_Collaborative_Filtering.md)
4. [04_Hybrid_Cold_Start_Da_Dang.md](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/LyThuyet/04_Hybrid_Cold_Start_Da_Dang.md)
5. [05_Ranking_Reranking_Sections.md](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/LyThuyet/05_Ranking_Reranking_Sections.md)
6. [06_Du_Lieu_Su_Kien_Materialize_Danh_Gia.md](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/LyThuyet/06_Du_Lieu_Su_Kien_Materialize_Danh_Gia.md)

## 4. Thứ tự đọc code

### Bước 1: Đọc file xếp hạng chính

- [BffCatalogController.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs)

Bạn cần chú ý:

- `home`, `search`, `similar` được ghép tín hiệu như thế nào.
- Khi nào dùng content-based, khi nào dùng hybrid.
- Vì sao có các section như `for_you`, `today_highlights`, `buy_again`, `recent_shop`, `favorite_shop`, `seasonal_local`.
- Vì sao hệ thống còn làm diversity theo seller/origin/category.

### Bước 2: Đọc file trả signal từ Ordering

- [ProductInsightsController.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs)

Bạn cần chú ý:

- Endpoint nào trả `home-profile`, `home-collaborative`, `similar`, `search-ranking`.
- Endpoint nào trả `user-product`, `user-category`, `user-seller`.
- Khi không có signal thì fallback ra sao.

### Bước 3: Đọc service materialize affinity

- [RecommendationAffinityService.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs)

Đây là nơi trả lời:

- Hệ thống biến `view`, `search`, `click`, `purchase` thành score như thế nào.
- Vì sao lại có `RecommendationUserProductScore`, `RecommendationUserCategoryScore`, `RecommendationUserSellerScore`, `RecommendationBasketAffinity`, `RecommendationReplenishmentProfile`.

### Bước 4: Đọc cơ chế refresh nền

- [RecommendationAffinityRefreshSignal.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshSignal.cs)
- [RecommendationAffinityRefreshBackgroundService.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshBackgroundService.cs)

### Bước 5: Đọc các model dữ liệu

- [RecommendationUserProductScore.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationUserProductScore.cs)
- [RecommendationUserCategoryScore.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationUserCategoryScore.cs)
- [RecommendationUserSellerScore.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationUserSellerScore.cs)
- [RecommendationBasketAffinity.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationBasketAffinity.cs)
- [RecommendationReplenishmentProfile.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationReplenishmentProfile.cs)
- [RecommendationHomePreferenceSeed.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationHomePreferenceSeed.cs)
- [RecommendationHomeCollaborativeCandidate.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationHomeCollaborativeCandidate.cs)
- [RecommendationProductAffinity.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationProductAffinity.cs)
- [RecommendationSearchKeywordAffinity.cs](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationSearchKeywordAffinity.cs)

### Bước 6: Đọc script đánh giá output

- [run-recommendation-evaluation.ps1](D:/NCKH/DOAN/NCKH-FRESH-FARM/HocGoiY/output/recommendation-evaluation/run-recommendation-evaluation.ps1)

## 5. Bản đồ khái niệm

| Khái niệm | Ý nghĩa | Chỗ nên đọc |
|---|---|---|
| Content-based | Gợi ý dựa trên thuộc tính item | `BffCatalogController.cs` |
| Collaborative filtering | Gợi ý dựa trên người tương tự / item tương tự | `ProductInsightsController.cs`, `RecommendationAffinityService.cs` |
| Hybrid | Trộn nhiều nguồn điểm | `BffCatalogController.cs` |
| Cold-start | Người mới/chưa có lịch sử | `BffCatalogController.cs` |
| Diversity | Tránh top-k quá lặp seller/category/origin | `BffCatalogController.cs` |
| Rebuy / replenish | Gợi ý mua lại, đến kỳ mua lại | `BffCatalogController.cs`, `RecommendationReplenishmentProfile.cs` |
| Materialized score | Điểm đã tính sẵn và lưu lại | `RecommendationAffinityService.cs` |
| Evaluation | Đo chất lượng output runtime | `run-recommendation-evaluation.ps1` |

## 6. Nếu bạn chỉ có 1 buổi để học

Hãy đọc:

1. `01_Tổng_Quan_Hệ_Thống_Gợi_Ý.md`
2. `04_Hybrid_Cold_Start_Da_Dang.md`
3. `05_Ranking_Reranking_Sections.md`
4. `BffCatalogController.cs`

## 7. Cách học nhanh nhất

1. Đọc 1 file lý thuyết.
2. Mở 1 file code tương ứng.
3. Tự viết lại 1 ví dụ rất nhỏ bằng tay.
4. Quay lại code xem production thêm gì ngoài ví dụ sách giáo khoa.

Ví dụ:

- Hôm nay đọc `Content-Based Filtering`.
- Sau đó mở `BffCatalogController.cs`.
- Tự lấy 3 sản phẩm:
  - Rau muống
  - Cải xanh
  - Táo
- Tự cho điểm bằng tay theo category/origin.
- Rồi so với cách code đang cộng thêm recency, stock, seller, locality, diversity.
