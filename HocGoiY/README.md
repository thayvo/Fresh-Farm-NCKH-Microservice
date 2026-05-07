# Hoc Goi Y

Bo code recommendation trong du an nay chu yeu duoc viet bang:

- `C#`: logic API, scoring, ranking, section hoa recommendation, materialize signal.
- `PowerShell`: script danh gia nhanh ket qua runtime recommendation.

Thu tu doc de de hieu nhat:

1. `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
   File tong hop logic ranking o BFF cho `home`, `search`, `similar`.
2. `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs`
   File tra signal/personalization tu Ordering.
3. `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs`
   File materialize affinity, score va profile.
4. `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshSignal.cs`
   File phat tin hieu refresh sau event.
5. `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshBackgroundService.cs`
   File nen xu ly refresh affinity.
6. Cac file `Models/Recommendation*.cs`
   Day la cac bang/doi tuong du lieu recommendation.
7. `output/recommendation-evaluation/run-recommendation-evaluation.ps1`
   Script goi runtime de do nhanh output recommendation.

Ghi chu:

- Moi file trong thu muc nay da duoc them comment dau file de chi ro file goc trong repo.
- Day la ban sao hoc tap, khong phai noi chinh de sua code production.
