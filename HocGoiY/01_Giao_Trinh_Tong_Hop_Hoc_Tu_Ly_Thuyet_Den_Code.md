# Giao trinh hoc module goi y FreshFarm tu ly thuyet den code

Ngay lap: 2026-05-10

Tai lieu nay viet theo huong ban co the hoc duoc that, khong chi doc de biet. Noi dung gom:

- Nen hoc cai nao truoc, cai nao sau.
- Ly thuyet day du vua du de hieu code.
- Cong thuc toan chi tiet dua tren module goi y FreshFarm.
- Cach hoc theo co che tri nho: chia nho, goi nho chu dong, lap lai ngat quang, vi du tay, Feynman.
- File bai lam/source can mo o tung buoc.

## 0. Ban nen hieu module nay bang mot cau

Module goi y FreshFarm la pipeline:

```text
User event -> gom tin hieu -> tinh diem -> xep hang -> rerank -> hien thi -> do luong -> tuning
```

Trong du an:

- `Ordering` la noi thu thap va bien hanh vi thanh diem.
- `BFF` la noi lay diem do, tron voi du lieu san pham, roi quyet dinh hien thi gi.
- `Frontend` vua hien thi goi y, vua gui nguoc event de he thong hoc tiep.

Neu phai noi truoc hoi dong:

> He thong goi y cua FreshFarm ket hop content-based, collaborative filtering, ML.NET matrix factorization, session-aware reranking, business rules va A/B metrics de ca nhan hoa san pham tren trang chu, tim kiem va chi tiet san pham.

## 1. Cach hoc theo "tri nao hoc" de khong bi ngop

### 1.1 Nguyen tac 1: Hoc theo lop, khong hoc theo file

Dung mo ngay `BffCatalogController.cs` roi doc tu tren xuong. File do dai va gom nhieu tang logic.

Hay hoc theo 5 lop:

1. Bai toan: vi sao can goi y.
2. Du lieu: he thong biet gi ve user va item.
3. Cong thuc: diem duoc tinh nhu the nao.
4. Code: cong thuc nam o file nao.
5. Danh gia: biet no tot hay khong bang chi so nao.

Nao bo hoc tot hon khi co "khung" truoc, roi moi gan chi tiet vao khung.

### 1.2 Nguyen tac 2: Moi buoi chi hoc mot concept lon

Dung hoc content-based, collaborative, ML.NET, A/B test trong cung mot buoi.

Moi buoi chi can:

- Doc 1 concept.
- Lam 1 vi du tinh tay.
- Mo 1-2 file code lien quan.
- Tu noi lai bang loi cua minh.

### 1.3 Nguyen tac 3: Goi nho chu dong quan trong hon doc lai

Sau khi doc xong mot muc, dung nhin tai lieu va tu tra loi:

- Input cua cong thuc la gi?
- Output la gi?
- Trong code FreshFarm, no nam o file nao?
- Neu thieu du lieu thi fallback ra sao?

Neu tra loi khong duoc, quay lai doc. Day la cach hoc nhanh hon doc lap lai nhieu lan.

### 1.4 Nguyen tac 4: Hoc bang vi du 3 san pham

Moi cong thuc nen thu voi 3 item:

- Rau muong
- Cai xanh
- Tao nhap khau

Vi du nho giup nao thay duoc diem nao day item len, diem nao keo item xuong.

### 1.5 Nguyen tac 5: Lap lai ngat quang

Lich hoc nen nhu sau:

- Ngay 1: hoc tong quan + content-based.
- Ngay 2: on lai 15 phut, hoc collaborative.
- Ngay 3: on lai 20 phut, hoc hybrid/ranking.
- Ngay 5: hoc materialize + event.
- Ngay 7: hoc ML.NET + metrics.
- Ngay 10: tu thuyet minh lai ca pipeline.

Khoang cach giua cac lan on lam tri nho ben hon.

## 2. Lo trinh hoc hieu qua nhat

### Cap 1: Hieu bai toan va pipeline

Muc tieu:

- Biet module goi y lam gi.
- Biet `Ordering`, `BFF`, `Frontend` moi thang lam gi.

Doc:

- `HocGoiY/LyThuyet/10_Ly_Thuyet_Tong_Quan_He_Thong_Goi_Y.md`
- Muc 0 va muc 3 cua file nay.

Tu tra loi:

- Goi y khac tim kiem o dau?
- Vi sao khong chi lay san pham ban chay?
- Vi sao can event?

### Cap 2: Hoc content-based truoc

Ly do hoc truoc:

- De hieu nhat.
- Khong can user khac.
- Rat hop voi nong san vi san pham co category, origin, seasonality, seller.

Doc:

- `HocGoiY/LyThuyet/11_Ly_Thuyet_Content_Based_Filtering.md`
- Muc 4 cua file nay.

Mo code:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/product-page.js`

### Cap 3: Hoc collaborative filtering

Ly do hoc sau content-based:

- Can hieu ma tran user-item.
- Can hieu hanh vi nhieu user tao thanh tin hieu "mua chung", "xem chung", "click chung".

Doc:

- `HocGoiY/LyThuyet/12_Ly_Thuyet_Collaborative_Filtering.md`
- Muc 5 cua file nay.

Mo code:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs`

### Cap 4: Hoc hybrid, ranking, reranking

Ly do hoc o giua:

- Code production khong dung mot thuat toan duy nhat.
- FreshFarm tron nhieu diem va them business rule.

Doc:

- `HocGoiY/LyThuyet/13_Ly_Thuyet_Hybrid_Cold_Start_Da_Dang.md`
- `HocGoiY/LyThuyet/14_Ly_Thuyet_Ranking_Reranking_Chia_Section.md`
- Muc 6 va 7 cua file nay.

Mo code:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationReranker.cs`

### Cap 5: Hoc event, materialize, background refresh

Ly do hoc sau ranking:

- Ban da hieu can diem nao.
- Gio hoc diem do duoc sinh ra tu dau.

Doc:

- `HocGoiY/LyThuyet/15_Ly_Thuyet_Event_Materialize_Danh_Gia.md`
- Muc 8 cua file nay.

Mo code:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationEventsController.cs`
- `src/Web/FreshFarm.Web.Bff/Controllers/BffRecommendationEventsController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshBackgroundService.cs`

### Cap 6: Hoc ML.NET matrix factorization

Ly do hoc gan cuoi:

- Day la phan kho hon.
- Can biet user-item score va collaborative truoc.

Doc:

- Muc 9 cua file nay.

Mo code:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlTrainingService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMlController.cs`

### Cap 7: Hoc metrics, A/B test, rollout

Ly do hoc cuoi:

- Sau khi hieu he thong goi y sinh output, moi hoc cach do output co tot khong.

Doc:

- Muc 10 va 11 cua file nay.

Mo code:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMetricsController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/IRecommendationExperimentService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/MultiObjectiveRecommendationRolloutService.cs`

## 3. Ban do module theo code FreshFarm

| Lop | Y nghia | File chinh |
|---|---|---|
| Frontend tracking | Gui event view/search/click/impression | `wwwroot/js/site.js`, `home-page.js`, `search-page.js`, `product-page.js` |
| BFF event proxy | Gan session/user va forward event | `BffRecommendationEventsController.cs` |
| Ordering event API | Validate va luu event | `RecommendationEventsController.cs` |
| Materialize score | Tinh truoc affinity/profile | `RecommendationAffinityService.cs` |
| Product insights API | Tra signal cho BFF | `ProductInsightsController.cs` |
| BFF ranking | Tron diem va tao output | `BffCatalogController.cs` |
| Session rerank | Uu tien hanh vi gan day | `SessionAwareRecommendationReranker.cs`, `SessionSignalService.cs` |
| ML.NET | Matrix factorization user-product | `RecommendationMlTrainingService.cs` |
| Metrics | CTR, add-to-cart, purchase, revenue | `RecommendationMetricsService.cs` |
| Rollout | Tang/giam traffic an toan | `MultiObjectiveRecommendationRolloutService.cs` |

## 4. Content-based filtering: ly thuyet va cong thuc

### 4.1 Y tuong

Content-based filtering goi y item giong voi thu user tung thich.

Trong FreshFarm, item co feature:

- Danh muc: rau, trai cay, nam, cu...
- Xuat xu/vung trong.
- Tieu chuan: huu co, VietGAP...
- Don vi.
- Gia.
- Mua vu.
- Seller.
- Stock.

### 4.2 Bieu dien vector item

Moi san pham \(i\) duoc bieu dien thanh vector:

\[
x_i = [x_{i1}, x_{i2}, ..., x_{im}]
\]

Vi du:

```text
Rau muong Long An huu co
x = [rau_la=1, trai_cay=0, long_an=1, huu_co=1, gia_re=1, dung_mua=1]
```

### 4.3 User profile

Neu user \(u\) da tuong tac voi cac item \(I_u\), vector so thich:

\[
p_u = \frac{\sum_{j \in I_u} a_{uj}x_j}{\sum_{j \in I_u} a_{uj}}
\]

Trong do \(a_{uj}\) la do manh tuong tac:

- View: nhe.
- Search click: vua.
- Recommendation click: vua.
- Purchase: manh.

### 4.4 Cosine similarity

Diem content-based:

\[
CB(u,i)=\frac{p_u \cdot x_i}{||p_u||\,||x_i||}
\]

Neu \(CB\) gan 1 thi item hop gu user.

### 4.5 Vi du tinh tay

Gia su user thich:

\[
p_u=[0.9, 0.8, 0.1]
\]

Trong do:

- Feature 1: rau la.
- Feature 2: dia phuong.
- Feature 3: trai cay nhap.

Item A:

\[
x_A=[1,1,0]
\]

Item B:

\[
x_B=[0,0,1]
\]

Tich vo huong:

\[
p_u \cdot x_A = 0.9 + 0.8 + 0 = 1.7
\]

\[
p_u \cdot x_B = 0.1
\]

Item A phu hop hon B.

### 4.6 Mapping vao FreshFarm

Trong FreshFarm, content-based khong dung dung mot cong thuc cosine thuan tuy o moi cho. No duoc bien thanh cac heuristic/rule trong BFF:

- San pham cung category duoc uu tien.
- Cung origin/standard/seasonality duoc uu tien.
- San pham con hang duoc uu tien.
- San pham co reason/tag duoc dua ra UI.

No nam chu yeu trong:

- `BffCatalogController.cs`
- Endpoint `/bff/recommendations/products/{id}/similar`
- Endpoint `/bff/recommendations/home`

## 5. Collaborative filtering: ly thuyet va cong thuc trong FreshFarm

### 5.1 Y tuong

Collaborative filtering khong hoi item giong nhau ve noi dung, ma hoi:

> Nhung item nao thuong duoc nhieu user xem/click/mua cung nhau?

### 5.2 Ma tran user-item

Ta co ma tran:

\[
R_{u,i}
\]

Trong do \(R_{u,i}\) la do user \(u\) quan tam item \(i\).

Voi implicit feedback:

\[
R_{u,i}=w_vView_{u,i}+w_sSearchClick_{u,i}+w_rRecClick_{u,i}+w_pPurchase_{u,i}
\]

### 5.3 User-product score trong code

Trong `RecommendationAffinityService.cs`, diem user-product:

\[
UserProductScore =
42 \cdot PurchaseCount
+ 22 \cdot SearchClickCount
+ 18 \cdot RecommendationClickCount
+ 8 \cdot ViewCount
+ RecencyBoost
+ NegativePenalty
\]

Y nghia:

- Purchase nang nhat: 42 diem.
- Search click: 22 diem.
- Recommendation click: 18 diem.
- View: 8 diem.
- Hanh vi moi gan day duoc cong them.
- Negative feedback co the tru diem.

### 5.4 Recency boost

Code cong them:

\[
RecencyBoost=\max(0,18-\min(ageDays,18))
\]

Neu user vua tuong tac hom nay:

\[
ageDays=0 \Rightarrow RecencyBoost=18
\]

Neu da qua 18 ngay:

\[
ageDays \ge 18 \Rightarrow RecencyBoost=0
\]

### 5.5 Product affinity

Product affinity tra loi:

> Neu user quan tam san pham A, san pham B co nen duoc goi y theo A khong?

Trong code:

\[
Affinity(A,B)=32 \cdot CoPurchase(A,B)+14 \cdot CoClick(A,B)+6 \cdot CoView(A,B)
\]

Y nghia:

- Mua chung quan trong nhat.
- Click chung quan trong vua.
- Xem chung nhe hon.

### 5.6 Vi du tinh tay

Gia su voi cap `rau muong -> nam rom`:

- CoPurchase = 3
- CoClick = 5
- CoView = 10

\[
Affinity=32 \cdot 3 + 14 \cdot 5 + 6 \cdot 10
\]

\[
Affinity=96+70+60=226
\]

Neu `rau muong -> tao nhap`:

- CoPurchase = 0
- CoClick = 1
- CoView = 4

\[
Affinity=0+14+24=38
\]

Vay nam rom se duoc goi y manh hon tao nhap trong ngu canh lien quan rau muong.

### 5.7 Home collaborative score

Trong `ProductInsightsController.cs`, khi co seed preference:

\[
SeedWeight = 1 + \frac{\min(PreferenceScore,240)}{120}
\]

Sau do:

\[
CollaborativeScore += SignalScore \cdot SeedWeight
\]

Y nghia:

- Seed ma user thich manh thi cac item lien quan den seed do duoc day manh hon.
- `SeedWeight` toi da xap xi 3.

## 6. Hybrid recommendation: cong thuc tong hop

### 6.1 Vi sao can hybrid

Content-based tot cho cold-start nhung de lap gu cu.

Collaborative tot khi du lieu nhieu nhung kho voi user moi/item moi.

Business rules giup output dung thuc te: con hang, dung mua, seller phu hop, co giao hang.

### 6.2 Cong thuc tong quat

\[
Score(u,i)=
\alpha CB(u,i)
+\beta CF(u,i)
+\gamma ML(u,i)
+\delta Business(u,i)
+\eta Session(u,i)
\]

Trong do:

- \(CB\): content-based.
- \(CF\): collaborative/affinity.
- \(ML\): user-product score tu ML.NET/materialized score.
- \(Business\): stock, seasonality, seller, locality.
- \(Session\): hanh vi gan day.

### 6.3 Cong thuc production de nho

Nen hieu FreshFarm theo cong thuc:

\[
FinalScore = BaseScore + SessionBoost + ObjectiveBoost + StockBoost + LocalityBoost - Penalty
\]

Sau do moi:

1. Sort theo `FinalScore`.
2. Ap diversity seller/category/origin.
3. Chia thanh section.
4. Gan reason/tag.

### 6.4 Seasonality boost

Trong `ProductInsightsController.cs`, seasonality duoc cong vao cac diem:

\[
PreferenceScore' = PreferenceScore + SeasonalityScore \cdot m
\]

\[
CollaborativeScore' = CollaborativeScore + SeasonalityScore \cdot m
\]

\[
HybridSearchScore' = HybridSearchScore + SeasonalityScore \cdot m
\]

Voi:

- \(m=0.12\) cho personalized.
- \(m=0.24\) cho fallback.

Y nghia:

- Khi da co ca nhan hoa, seasonality chi la boost phu.
- Khi fallback, seasonality quan trong hon.

## 7. Ranking, reranking, diversity va section

### 7.1 Ranking

Ranking la tinh diem goc:

\[
BaseScore_i = f(CB_i, CF_i, ML_i, Business_i)
\]

Sau do sap xep:

\[
rank = argsort(-BaseScore)
\]

### 7.2 Reranking

Reranking sua lai danh sach de UX tot hon:

\[
FinalList = Rerank(SortedList, Constraints)
\]

Constraints co the la:

- Khong de mot seller chiem qua nhieu slot.
- Khong de toan cung category.
- Chen item mua lai neu can.
- Chen item dung mua/dia phuong.

### 7.3 Diversity penalty truc giac

Mot cong thuc de hieu:

\[
AdjustedScore_i = Score_i - \lambda_s CountSeller_i - \lambda_c CountCategory_i
\]

Neu seller da xuat hien nhieu trong top-k, item tiep theo cung seller bi giam diem.

### 7.4 Section

Section la chia output thanh cac ly do de user hieu:

- Goi y cho ban hom nay.
- San pham tuong tu.
- San pham ban chay.
- San pham moi.
- Co the mua lai.
- Mua kem.

Trong UI, section giup user khong cam thay recommendation "tu tren troi roi xuong".

## 8. Event, materialize va data pipeline

### 8.1 Event

Event la hanh vi tho:

- Product view.
- Search.
- Search click.
- Recommendation impression.
- Recommendation click.
- Add to cart.
- Purchase.

### 8.2 Tai sao can materialize

Khong nen moi request lai quet tat ca event.

Ta tinh truoc:

\[
MaterializedScore = Aggregate(Events)
\]

Roi request chi doc bang score da tinh.

### 8.3 Home preference seed

Trong code:

\[
PreferenceScore =
10 \cdot ViewCount
+ 28 \cdot SearchClickCount
+ 22 \cdot RecommendationClickCount
+ 35 \cdot PurchaseCount
\]

So sanh voi user-product score:

- Home preference seed nhan manh search click va purchase gan voi session/home.
- User-product score nhan manh purchase hon va them recency/negative feedback.

### 8.4 Search keyword affinity

Trong code:

\[
HybridSearchScore =
20 \cdot SearchClickCount
+ 14 \cdot SearchRecommendationClickCount
+ 6 \cdot SearchViewSessionCount
\]

Y nghia:

- Neu keyword "rau huu co" thuong dan den click san pham A, A se duoc day len khi tim keyword do.

### 8.5 Basket affinity

Trong code:

\[
BasketScore =
30 \cdot CoPurchaseOrderCount
+ 6 \cdot CoClickSessionCount
\]

Y nghia:

- Mua chung la tin hieu manh nhat cho "mua kem".

### 8.6 Replenishment score

Replenishment tra loi: "Da den luc mua lai chua?"

Trong code:

\[
ReplenishmentScore = 14 \cdot PurchaseCount + TimingScore + FrequencyBoost
\]

Neu co chu ky mua lai:

\[
TimingScore = \max(0,30-|ExpectedReorderDate-Now|)
\]

Neu chua du chu ky:

\[
TimingScore = \max(0,14-\min(DaysSinceLastPurchase,14))
\]

Frequency boost:

\[
FrequencyBoost=\max(0,18-\min(AverageRepurchaseDays,18))
\]

Y nghia:

- Mua nhieu lan thi diem cao.
- Gan ngay du kien mua lai thi diem cao.
- Chu ky mua lai ngan thi diem cao hon.

## 9. ML.NET Matrix Factorization

### 9.1 Matrix factorization la gi

Ta co ma tran user-item \(R\), nhung rat thua:

\[
R \approx P Q^T
\]

Trong do:

- \(P_u\): vector an cua user.
- \(Q_i\): vector an cua item.

Diem du doan:

\[
\hat r_{ui}=P_u \cdot Q_i
\]

### 9.2 Dataset implicit feedback trong code

Trong `RecommendationMlTrainingService.cs`, label duoc tinh tu:

\[
Label =
ViewCount \cdot ViewWeight \cdot Decay
+ SearchClickCount \cdot SearchClickWeight \cdot Decay
+ RecommendationClickCount \cdot RecommendationClickWeight \cdot Decay
+ PurchaseCount \cdot PurchaseWeight \cdot Decay
+ NegativeSignal
\]

### 9.3 Decay trong ML.NET

Code dung half-life 30 ngay:

\[
Decay = 0.5^{\frac{ageDays}{30}}
\]

Neu event moi hom nay:

\[
ageDays=0 \Rightarrow Decay=1
\]

Neu event cach 30 ngay:

\[
Decay=0.5
\]

Neu event cach 60 ngay:

\[
Decay=0.25
\]

Y nghia:

- Hanh vi cu van co gia tri nhung yeu dan.

### 9.4 One-class matrix factorization

Code dung:

```text
MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass
```

Nghia la phu hop voi implicit feedback: user khong rating sao ro rang, chi co hanh vi.

### 9.5 Normalize model score

Model score raw duoc dua ve khoang de de tron voi diem khac:

\[
Normalized =
floor + \frac{score-min}{max-min}(ceiling-floor)
\]

Neu tat ca score bang nhau thi dung rank score:

\[
RankScore = ceiling - \frac{rank-1}{count-1}(ceiling-floor)
\]

Y nghia:

- Diem ML duoc dua ve thang diem on dinh.
- Top item van co diem cao hon item duoi.

## 10. Session-aware reranking

### 10.1 Khac gi ML

ML hoc tu lich su dai hon.

Session-aware rerank phan ung voi hanh vi vua xay ra:

- Vua tim keyword nao.
- Vua click category nao.
- Vua click seller nao.
- Dang o dia chi/khu vuc nao.

### 10.2 Cong thuc trong code

Trong `SessionAwareRecommendationReranker.cs`:

\[
FinalScore = BaseScore + ObjectiveScore + NegativePenalty + SessionBoost + StockBoost + NearbyBoost
\]

Trong do:

\[
SessionBoost =
KeywordHits \cdot SearchKeywordBoost
+ RecentProductClickBoost
+ RecentSellerClickBoost
+ RecentCategoryClickBoost
\]

Sau do:

\[
Score += SessionBoost \cdot PositionBoostFactor
\]

Stock:

\[
Score += InStockBoost
\]

Neu het hang:

\[
Score -= OutOfStockPenalty
\]

Gan khu vuc:

\[
Score += NearbyShopBoost
\]

### 10.3 Objective score

Objective score tron CTR, add-to-cart, purchase, revenue:

\[
ObjectiveScore =
\frac{
w_{ctr}CTR'
+w_{cart}CartRate'
+w_{purchase}PurchaseRate'
+w_{revenue}Revenue'
}{
w_{ctr}+w_{cart}+w_{purchase}+w_{revenue}
}
\cdot Scale
\]

Dau phay `'` la gia tri da normalize ve 0-1.

Y nghia:

- San pham khong chi hop gu, ma con phai co hieu qua that.

## 11. Metrics, A/B test va rollout

### 11.1 CTR

\[
CTR = \frac{Clicks}{Impressions}
\]

Neu 1000 impression, 80 click:

\[
CTR=0.08=8\%
\]

### 11.2 Add-to-cart rate

\[
AddToCartRate = \frac{AddToCarts}{Impressions}
\]

### 11.3 Purchase rate

\[
PurchaseRate = \frac{Purchases}{Impressions}
\]

### 11.4 Revenue per impression

\[
RPI = \frac{Revenue}{Impressions}
\]

### 11.5 A/B test trong FreshFarm

Nhom:

- A: ML.
- B: Session rerank.

So sanh:

\[
Uplift = Metric_B - Metric_A
\]

Phan tram uplift:

\[
Uplift\% = \frac{Metric_B - Metric_A}{Metric_A} \cdot 100
\]

### 11.6 Guardrail rollout

He thong rollout khong chi nhin CTR. No can giu:

- Purchase rate khong giam manh.
- Revenue per impression khong giam.
- Du impression moi quyet dinh.

Tu duy:

```text
Neu metric tot va on dinh -> tang traffic
Neu revenue/purchase xau -> rollback
Neu CTR xau -> chinh trong so
Neu thieu data -> giu nguyen
```

## 12. Lich hoc 10 ngay de hieu va thuyet minh duoc

### Ngay 1: Tong quan

Hoc:

- Pipeline.
- BFF/Ordering/Frontend.

Bai tap:

- Ve lai pipeline bang 6 hop.
- Giai thich bang 5 cau.

### Ngay 2: Content-based

Hoc:

- Item vector.
- User profile.
- Cosine similarity.

Bai tap:

- Tao vector cho 3 san pham.
- Tinh diem content-based bang tay.

### Ngay 3: Collaborative

Hoc:

- User-item matrix.
- Co-purchase, co-click, co-view.
- Product affinity.

Bai tap:

- Tinh `Affinity = 32*CoPurchase + 14*CoClick + 6*CoView` cho 3 cap item.

### Ngay 4: Hybrid va ranking

Hoc:

- Hybrid score.
- Ranking/reranking.
- Diversity.

Bai tap:

- Cho 5 item co score va seller.
- Ap seller cap bang tay.

### Ngay 5: Event va materialize

Hoc:

- Event.
- Materialized score.
- Background refresh.

Bai tap:

- Tu 5 event tinh `PreferenceScore`.
- Noi event nao vao bang nao.

### Ngay 6: Home recommendation

Hoc:

- Candidate pool.
- ML/collaborative/content source.
- Reason/tag.

Bai tap:

- Chon 8 san pham top cho user gia lap.
- Gan ly do cho tung san pham.

### Ngay 7: Similar va search

Hoc:

- Product similar.
- Search ranking.
- Keyword affinity.

Bai tap:

- Voi keyword "rau huu co", tinh `HybridSearchScore` cho 3 item.

### Ngay 8: ML.NET

Hoc:

- Matrix factorization.
- Label implicit feedback.
- Decay.

Bai tap:

- Tinh decay cho event 0, 30, 60 ngay.
- Giai thich vi sao user moi kho dung ML.

### Ngay 9: Metrics va A/B

Hoc:

- CTR.
- Purchase rate.
- Revenue per impression.
- Uplift.

Bai tap:

- Cho A/B co impression/click/purchase/revenue, tinh metric va ket luan.

### Ngay 10: Tong hop thuyet minh

Bai tap:

- Trinh bay 5 phut khong nhin tai lieu.
- Ve pipeline.
- Viet lai 5 cong thuc quan trong.
- Noi file code chinh cho tung cong thuc.

## 13. Nam cong thuc quan trong nhat can thuoc

### 13.1 Content-based

\[
CB(u,i)=\frac{p_u \cdot x_i}{||p_u||\,||x_i||}
\]

### 13.2 Product affinity

\[
Affinity=32CoPurchase+14CoClick+6CoView
\]

### 13.3 Home preference

\[
PreferenceScore=10View+28SearchClick+22RecClick+35Purchase
\]

### 13.4 User-product score

\[
UserProductScore=42Purchase+22SearchClick+18RecClick+8View+Recency+Penalty
\]

### 13.5 ML decay

\[
Decay=0.5^{ageDays/30}
\]

### 13.6 Matrix factorization

\[
\hat r_{ui}=p_u \cdot q_i
\]

### 13.7 Session rerank

\[
FinalScore=BaseScore+ObjectiveScore+SessionBoost+StockBoost+NearbyBoost-Penalty
\]

### 13.8 CTR

\[
CTR=\frac{Clicks}{Impressions}
\]

## 14. Cach tu kiem tra ban da hieu chua

Ban hieu that neu tra loi duoc 10 cau nay:

1. Vi sao FreshFarm khong dung moi san pham ban chay?
2. Content-based can feature nao cua san pham?
3. Collaborative khac content-based o dau?
4. Vi sao purchase co trong so cao hon view?
5. Materialize score de lam gi?
6. `Affinity=32CoPurchase+14CoClick+6CoView` nghia la gi?
7. ML.NET matrix factorization du doan cai gi?
8. Session-aware rerank khac ML o dau?
9. CTR cao co chac recommendation tot khong?
10. Khi nao nen rollback rollout?

Neu bi ket cau nao, quay lai muc tuong ung trong tai lieu.

## 15. Cach viet vao bao cao

Co the viet:

> Module goi y duoc thiet ke theo kien truc hybrid recommendation. He thong thu thap cac hanh vi nhu xem san pham, tim kiem, click, impression va mua hang. Cac hanh vi nay duoc materialize thanh nhieu bang diem nhu user-product score, product affinity, search keyword affinity, basket affinity va replenishment profile. Lop BFF ket hop cac tin hieu content-based, collaborative, ML.NET matrix factorization va business rules de tao danh sach goi y cho trang chu, tim kiem va chi tiet san pham. Sau khi ranking, he thong tiep tuc rerank theo session, ton kho, shop gan khu vuc va diversity. Chat luong goi y duoc do bang CTR, add-to-cart rate, purchase rate va revenue per impression thong qua A/B testing.

## 16. Cach thuyet minh bang loi noi

Noi ngan gon:

> Dau tien em ghi nhan user xem, tim, click va mua gi. Sau do em gom cac hanh vi nay thanh diem. Diem mua hang cao hon click, click cao hon view. Tu do em tao cac bang nhu user-product score, product affinity va keyword affinity. Khi user vao trang chu, BFF lay cac diem nay, ket hop voi thong tin san pham nhu category, seasonality, stock va seller de xep hang. Cuoi cung em rerank de danh sach khong bi lap seller, co ly do hien thi, va co tracking de do CTR/conversion.

## 17. Thu tu mo file khi hoc code

Dung thu tu nay:

1. `HocGoiY/LyThuyet/10_Ly_Thuyet_Tong_Quan_He_Thong_Goi_Y.md`
2. `HocGoiY/LyThuyet/11_Ly_Thuyet_Content_Based_Filtering.md`
3. `HocGoiY/LyThuyet/12_Ly_Thuyet_Collaborative_Filtering.md`
4. `HocGoiY/LyThuyet/13_Ly_Thuyet_Hybrid_Cold_Start_Da_Dang.md`
5. `HocGoiY/LyThuyet/14_Ly_Thuyet_Ranking_Reranking_Chia_Section.md`
6. `HocGoiY/LyThuyet/15_Ly_Thuyet_Event_Materialize_Danh_Gia.md`
7. `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
8. `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs`
9. `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs`
10. `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlTrainingService.cs`
11. `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationReranker.cs`
12. `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsService.cs`

## 18. Loi khuyen cuoi

Dung co hoc module nay theo kieu doc het file code dai. Hay hoc theo cau hoi:

- He thong can goi y cai gi?
- Du lieu nao cho biet user thich gi?
- Cong thuc nao bien hanh vi thanh diem?
- BFF tron diem va hien thi ra sao?
- Lam sao biet no tot hon?

Khi tra loi duoc 5 cau do, code dai den dau cung chi la chi tiet thuc thi.

