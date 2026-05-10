# Tai lieu cac phan da lam trong module goi y FreshFarm

Ngay lap: 2026-05-10

Muc tieu cua tai lieu nay la ghi lai theo tung phan nho nhung gi da duoc xay dung trong phan goi y cua du an FreshFarm, dua tren source hien co. Tai lieu co the dung de thuyet minh bao cao, giai thich luong xu ly, hoac lam checklist khi demo.

## Lo trinh doc theo cap do

Phan nay giup doc tai lieu theo dung thu tu, tu muc de nhat de nam y tuong den muc chuyen sau de hieu cong thuc, ML, do luong va rollout.

### Cap do 1: Tong quan de thuyet minh nhanh

Muc tieu:

- Hieu phan goi y nay giai quyet bai toan gi.
- Co the trinh bay ngan gon trong bao cao hoac demo.
- Chua can doc sau vao cong thuc diem hay ML.

Can doc:

1. `14. Tom tat ngan de dua vao bao cao`
2. `5. Goi y trang chu trong BFF`
3. `6. Goi y san pham tuong tu`
4. `7. Hybrid ranking cho tim kiem`
5. `11. UI integration va tracking tren frontend`

Nen nam duoc:

- He thong khong con hien san pham ngau nhien/danh sach tinh.
- Trang chu, search va trang chi tiet deu co goi y rieng.
- Goi y co ly do hien thi, co track impression/click de do hieu qua.

File nen xem truoc:

- `src/Web/FreshFarm.Web.Bff/wwwroot/js/home-page.js`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/product-page.js`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/search-page.js`

### Cap do 2: Hieu luong du lieu tu UI ve backend

Muc tieu:

- Hieu vi sao he thong co du lieu de hoc goi y.
- Hieu cac event nao duoc ghi nhan khi user xem, tim, click san pham.
- Hieu vai tro cua BFF khi forward event ve Ordering.

Can doc:

1. `1. Nen tang du lieu hanh vi nguoi dung`
2. `11. UI integration va tracking tren frontend`
3. `2. API Product Insights trong Ordering`

Nen nam duoc:

- Frontend goi `/bff/events/...`.
- BFF gan `SessionId`, lay `UserId` neu co dang nhap.
- Ordering luu event vao cac bang view/search/click/impression.
- Cac event nay la dau vao cho collaborative filtering va metrics.

File nen xem:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffRecommendationEventsController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationEventsController.cs`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/site.js`

### Cap do 3: Hieu cac API goi y dang phuc vu man hinh

Muc tieu:

- Hieu BFF lay du lieu goi y tu dau.
- Hieu Ordering cung cap nhung insight nao.
- Co the giai thich endpoint nao phuc vu trang nao.

Can doc:

1. `2. API Product Insights trong Ordering`
2. `5. Goi y trang chu trong BFF`
3. `6. Goi y san pham tuong tu`
4. `7. Hybrid ranking cho tim kiem`

Nen nam duoc:

- Home dung `/bff/recommendations/home`.
- Product detail dung `/bff/recommendations/products/{id}/similar`.
- Search dung `/bff/product-search` va co hybrid ranking khi sort `related`.
- BFF vua lay san pham tu Catalog, vua lay diem/tin hieu tu Ordering.

File nen xem:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs`

### Cap do 4: Hieu tinh diem va materialized recommendation

Muc tieu:

- Hieu vi sao can tinh truoc diem goi y.
- Hieu cac bang affinity/preference dung de tang toc va ca nhan hoa.
- Hieu cong thuc diem o muc y tuong.

Can doc:

1. `3. Materialized Affinity va cac bang diem goi y`
2. `2. API Product Insights trong Ordering`
3. `13. Kiem thu da co trong source`

Nen nam duoc:

- Co-purchase, co-click, co-view tao product affinity.
- User-product, user-category, user-seller tao preference theo user.
- Basket affinity phuc vu mua kem.
- Replenishment profile phuc vu mua lai.
- Background service rebuild cac bang diem de request khong phai tinh lai tu dau.

File nen xem:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshBackgroundService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshSignal.cs`

### Cap do 5: Hieu ML.NET va session-aware rerank

Muc tieu:

- Hieu phan ML collaborative filtering.
- Hieu phan rerank theo hanh vi gan day trong session.
- Phan biet diem ML dai han voi session-aware ngan han.

Can doc:

1. `4. ML.NET Matrix Factorization`
2. `8. Session-aware reranking`
3. `5. Goi y trang chu trong BFF`

Nen nam duoc:

- ML.NET dung Matrix Factorization de sinh diem user-product.
- Dataset ML lay tu view, search click, recommendation click va purchase.
- Session-aware rerank doc recent searches/recent clicks tu Redis/distributed cache.
- ML la xu huong dai han, session rerank la phan ung theo hanh vi vua xay ra.

File nen xem:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlTrainingService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMlController.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionSignalService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationReranker.cs`

### Cap do 6: Hieu do luong, A/B test va rollout an toan

Muc tieu:

- Hieu cach danh gia recommendation co tot hon hay khong.
- Hieu cach chia nhom A/B.
- Hieu cach rollout/tuning de tranh lam xau metric.

Can doc:

1. `9. A/B test va do luong hieu qua`
2. `10. Rollout va tuning tu dong`
3. `13. Kiem thu da co trong source`

Nen nam duoc:

- Nhom A la ML-only, nhom B la Session rerank.
- Metrics gom impression, click, add-to-cart, purchase, revenue.
- Co CTR, CTR theo vi tri, purchase rate, revenue per impression.
- Rollout co guardrail: metric xau thi rollback/giam trong so, on dinh thi tang traffic.

File nen xem:

- `src/Web/FreshFarm.Web.Bff/Services/IRecommendationExperimentService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMetricsController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/MultiObjectiveRecommendationRolloutService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationWeightTuningService.cs`

### Cap do 7: Hieu cau hinh runtime va test

Muc tieu:

- Hieu cac service duoc dang ky o dau.
- Hieu background job nao dang chay.
- Biet test nao bao ve phan recommendation.

Can doc:

1. `12. Cau hinh va dang ky DI/background service`
2. `13. Kiem thu da co trong source`

Nen nam duoc:

- Ordering dang ky affinity service, ML service, metrics service.
- BFF dang ky session signal, reranker, experiment, metrics client, rollout/tuning.
- Redis/distributed cache phuc vu session signal.
- Test da co cho event, metrics, ML, reranker, experiment va rollout.

File nen xem:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Program.cs`
- `src/Web/FreshFarm.Web.Bff/Program.cs`
- `src/Tests/FreshFarm.Ordering.Api.Tests`
- `src/Tests/FreshFarm.Web.Bff.Tests`

### Thu tu doc khuyen nghi neu chi co it thoi gian

Neu chi can bao cao nhanh:

1. Cap do 1
2. Cap do 2
3. Cap do 3

Neu can bao ve truoc hoi dong/giang vien:

1. Cap do 1
2. Cap do 2
3. Cap do 3
4. Cap do 4
5. Cap do 6

Neu can tiep tuc code/sua module:

1. Cap do 2
2. Cap do 3
3. Cap do 4
4. Cap do 5
5. Cap do 7

## 1. Nen tang du lieu hanh vi nguoi dung

Phan nay duoc lam de he thong khong chi goi y dua tren danh sach san pham tinh, ma co du lieu hanh vi that cua nguoi dung va phien truy cap.

Da bo sung cac nhom su kien recommendation trong Ordering:

- Luot xem san pham: `ProductViewEvent`.
- Su kien tim kiem: `SearchEvent`.
- Click vao ket qua tim kiem: `SearchClickEvent`.
- Impression cua block goi y: `RecommendationImpressionEvent`.
- Click vao san pham trong block goi y: `RecommendationClickEvent`.

Endpoint ghi nhan su kien nam o:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationEventsController.cs`
- `src/Web/FreshFarm.Web.Bff/Controllers/BffRecommendationEventsController.cs`

Luong xu ly:

1. Trinh duyet goi cac endpoint `/bff/events/...`.
2. BFF dam bao co `SessionId` on dinh cho ca guest.
3. Neu nguoi dung da dang nhap, BFF/Ordering gan them `UserId`.
4. BFF forward payload ve Ordering.
5. Ordering validate payload, luu event vao database, roi kich hoat refresh signal khi can.

Gia tri cua phan nay:

- Tao du lieu dau vao cho collaborative filtering.
- Tao du lieu do luong CTR/click/conversion.
- Cho phep ca nhan hoa theo user hoac theo session.

## 2. API Product Insights trong Ordering

Phan nay la lop API doc insight tu du lieu order, review va event de tra ve tin hieu ranking cho BFF.

File chinh:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs`

Da xay dung cac endpoint:

- `GET /api/orders/product-insights/home-profile`: lay seed so thich cho trang chu theo session/user.
- `GET /api/orders/product-insights/home-collaborative`: lay ung vien collaborative cho trang chu.
- `GET /api/orders/product-insights/stats`: lay thong ke san pham nhu da ban, danh gia, review.
- `GET /api/orders/product-insights/similar`: lay tin hieu san pham tuong tu theo co-view, co-click, co-purchase.
- `GET /api/orders/product-insights/search-ranking`: lay tin hieu xep hang tim kiem theo keyword.
- `GET /api/orders/product-insights/user-product`: lay diem user-product da materialize.
- `GET /api/orders/product-insights/user-seller`: lay diem user-seller.
- `GET /api/orders/product-insights/user-category`: lay diem user-category.
- `GET /api/orders/product-insights/basket-affinity`: goi y san pham thuong mua chung.
- `GET /api/orders/product-insights/replenishment-profile`: goi y mua lai san pham tieu hao/dinh ky.

Gia tri cua phan nay:

- Tach phan tinh insight ra khoi UI.
- BFF co the goi Ordering de lay cac tin hieu ranking thay vi tu tinh toan het trong frontend.
- Co fallback khi chua co du lieu materialized.

## 3. Materialized Affinity va cac bang diem goi y

Phan nay duoc lam de tinh truoc cac diem goi y, tranh moi request deu phai quet nhieu bang lon.

File chinh:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshBackgroundService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshSignal.cs`

Da tinh va luu cac nhom diem:

- Product affinity: san pham A co lien quan san pham B dua tren mua chung, xem chung, click chung.
- Search keyword affinity: keyword nao thuong dan den san pham nao.
- User-product score: user co xu huong thich san pham nao.
- User-category score: user thich nhom danh muc nao.
- User-seller score: user co xu huong mua/xem shop nao.
- Basket affinity: san pham nao nen goi y mua kem.
- Replenishment profile: san pham nao co kha nang can mua lai.
- Home preference seed: san pham lam hat giong so thich tren trang chu.
- Home collaborative candidate: san pham ung vien tu collaborative filtering cho trang chu.

Cong thuc diem dang dung:

- Co-purchase co trong so cao nhat.
- Click co trong so trung binh.
- View co trong so nhe hon.
- Purchase/search click/recommendation click/view duoc cong diem khac nhau tuy ngu canh.
- Negative feedback co the tru diem khi user thay nhieu nhung khong click/mua.

Gia tri cua phan nay:

- Tang toc do phuc vu recommendation.
- Tao du lieu nen cho home, search, product detail va basket.
- Cho phep rebuild dinh ky bang background service.

## 4. ML.NET Matrix Factorization

Phan nay duoc lam de bo sung lop ML collaborative filtering, thay vi chi dung rule/heuristic.

File chinh:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlTrainingService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlRefreshBackgroundService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMlController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Options/RecommendationMlOptions.cs`

Da xay dung:

- Dataset implicit feedback tu view, search click, recommendation click va purchase.
- Trong so tin hieu cau hinh duoc qua options.
- Giam trong so theo thoi gian bang decay.
- Negative feedback thanh weak negative signal.
- Train model `MatrixFactorization` cua ML.NET.
- Sinh diem du doan user-product.
- Co the materialize diem vao `RecommendationUserProductScores`.
- Co endpoint noi bo de xem status va rebuild thu cong.

Gia tri cua phan nay:

- Tao nguon ung vien `ML` cho goi y trang chu.
- Ca nhan hoa tot hon cho user da co lich su.
- Co co che skip khi du lieu chua du, tranh train model vo nghia.

## 5. Goi y trang chu trong BFF

Phan nay la noi BFF ket hop nhieu nguon de tra ve `Goi y cho ban hom nay`.

File chinh:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- Endpoint: `GET /bff/recommendations/home`

Da xay dung:

- Lay san pham tu Catalog lam candidate pool.
- Lay user-product ML score tu Ordering.
- Lay home preference seed tu Ordering.
- Lay home collaborative candidate tu Ordering.
- Ket hop content-based, collaborative va ML thanh hybrid ranking.
- Ap dung seller cap de tranh mot shop chiem qua nhieu slot.
- Bo trung san pham giua cac block.
- Them reason/tags de UI hien thi ly do goi y.
- Gan metadata `algorithm`, `signalSource`, `fallbackReason`.
- Track impression cho top san pham de phuc vu do luong.

Nguon ung vien trong trang chu:

- `ML`: diem user-product tu ML.NET/materialized score.
- `Collaborative`: san pham gan voi hanh vi user/session.
- `Content`: fallback dua tren catalog, category, san pham moi, ban chay, tin hieu san pham.

Gia tri cua phan nay:

- Trang chu khong con chi cat danh sach san pham chung.
- User moi van co fallback.
- User co lich su co goi y ca nhan hoa hon.

## 6. Goi y san pham tuong tu

Phan nay dung cho trang chi tiet san pham.

File chinh:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/product-page.js`
- Endpoint: `GET /bff/recommendations/products/{id}/similar`

Da xay dung:

- Lay san pham hien tai va candidate tu Catalog.
- Tinh do tuong tu content-based dua tren danh muc, ten, xuat xu, tieu chuan, don vi, mo ta.
- Lay signal tu Ordering endpoint `similar`.
- Blend content score voi collaborative score.
- Uu tien san pham con hang, co thong tin tot, phu hop voi ngu canh.
- UI product detail hien thi section `Goi y san pham tuong tu`.
- Track impression/click cho placement `product_similar`.

Gia tri cua phan nay:

- Tang kha nang kham pha san pham lien quan.
- Dung duoc ca khi user chua dang nhap.
- Co do luong click/impression rieng cho block tuong tu.

## 7. Hybrid ranking cho tim kiem

Phan nay lam cho trang search khong chi sap xep bang keyword match, ma co them hanh vi va so thich.

File chinh:

- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/search-page.js`
- Endpoint: `GET /bff/product-search`

Da xay dung:

- Search co filter theo danh muc, shop, ton kho, khu vuc giao, xuat xu, tieu chuan, don vi, gia, rating, preset.
- Khi sort la `related`, BFF co the ap dung hybrid search ranking.
- Lay keyword affinity tu Ordering qua `search-ranking`.
- Lay user-category va user-seller preference de ca nhan hoa ket qua.
- Bo sung seasonality badge/reason vao payload.
- Track search event va search click.
- Block related tren search co impression/click tracking.

Gia tri cua phan nay:

- Ket qua search gan hon voi hanh vi that.
- Neu user hay quan tam mot category/shop, ket qua co the duoc uu tien hon.
- Van giu duoc filter/sort/pagination hien co.

## 8. Session-aware reranking

Phan nay duoc lam de phan ung nhanh voi hanh vi gan day trong phien hien tai.

File chinh:

- `src/Web/FreshFarm.Web.Bff/Services/SessionSignalService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationReranker.cs`
- `src/Web/FreshFarm.Web.Bff/Options/SessionAwareRecommendationOptions.cs`

Da xay dung:

- Luu recent searches va recent clicks vao distributed cache/Redis theo user.
- Khi render goi y, reranker doc signal gan day.
- Boost san pham neu khop keyword vua tim.
- Boost san pham/shop/category vua click.
- Cong diem neu san pham con hang.
- Tru diem neu het hang.
- Cong diem neu shop gan khu vuc giao hang.
- Lay objective metrics va negative feedback de dieu chinh diem.
- Gioi han thoi gian rerank bang budget ngan de khong lam cham request.

Gia tri cua phan nay:

- Neu user vua tim "rau huu co", goi y co the nghieng ve rau huu co ngay.
- Neu user vua click mot shop/category, san pham lien quan duoc day len.
- Neu cache/Redis loi, he thong fallback ve base score.

## 9. A/B test va do luong hieu qua

Phan nay duoc lam de so sanh goi y ML-only voi session-aware rerank.

File chinh:

- `src/Web/FreshFarm.Web.Bff/Services/IRecommendationExperimentService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMetricsController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsDao.cs`

Da xay dung:

- Nhom A: `ML`.
- Nhom B: `Session`.
- User dang nhap duoc chia nhom on dinh bang hash user id.
- Guest duoc chia nhom va luu cookie.
- Track impression, click, add-to-cart, purchase.
- Bao cao CTR.
- Bao cao CTR theo vi tri.
- Bao cao objective metrics: CTR, add-to-cart rate, purchase rate, revenue per impression.
- Bao cao negative feedback.

Gia tri cua phan nay:

- Khong chi "cam thay goi y tot hon", ma co chi so do.
- Co the quyet dinh rollout dua tren conversion va revenue.
- Co attribution tu impression/click sang add-to-cart/purchase.

## 10. Rollout va tuning tu dong

Phan nay duoc lam de tang dan traffic cho session-aware recommendation va dieu chinh trong so dua tren metric.

File chinh:

- `src/Web/FreshFarm.Web.Bff/Services/MultiObjectiveRecommendationRolloutService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/MultiObjectiveRecommendationRolloutBackgroundService.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationWeightTuningService.cs`
- `src/Web/FreshFarm.Web.Bff/App_Data/session-aware-rerank-tuning.json`

Da xay dung:

- Monitor metric theo khung gio ngan.
- So sanh baseline A/ML voi variant B/Session.
- Neu du lieu chua du thi giu nguyen.
- Neu purchase rate hoac revenue per impression giam qua nguong thi rollback.
- Neu CTR giam thi co the giam revenue weight va phan bo lai trong so.
- Neu on dinh thi ramp traffic theo cac moc cau hinh.
- Khi dat 100% va on dinh du thoi gian thi lock rollout.
- Luu state, versions va lich su run vao file tuning.

Gia tri cua phan nay:

- Giam rui ro khi bat tinh nang goi y moi.
- Co lich su cau hinh de rollback.
- Phu hop voi huong multi-objective: khong toi uu CTR ma bo qua purchase/revenue.

## 11. UI integration va tracking tren frontend

Phan nay ket noi recommendation vao cac man hinh nguoi dung.

File chinh:

- `src/Web/FreshFarm.Web.Bff/wwwroot/js/site.js`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/home-page.js`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/search-page.js`
- `src/Web/FreshFarm.Web.Bff/wwwroot/js/product-page.js`

Da xay dung:

- Helper chung de tao `recommendationRunId`.
- Helper track product view.
- Helper track search.
- Helper track search click.
- Helper track recommendation impression.
- Helper track recommendation click.
- Home page goi `/bff/recommendations/home`.
- Home page track impression/click cho placement `home_today`.
- Product detail goi `/bff/recommendations/products/{id}/similar`.
- Product detail track impression/click cho `product_similar`.
- Search page track search event, search click va related recommendation.
- UI hien thi reason, tags va seasonality badge neu payload co.

Gia tri cua phan nay:

- Frontend vua hien thi goi y, vua tra nguoc du lieu de he thong hoc tiep.
- Moi placement co algorithm/run/rank ro rang de phan tich sau nay.

## 12. Cau hinh va dang ky DI/background service

Phan nay dam bao cac service recommendation chay duoc trong ASP.NET Core.

File chinh:

- `src/Services/Ordering/FreshFarm.Ordering.Api/Program.cs`
- `src/Web/FreshFarm.Web.Bff/Program.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/appsettings.json`
- `src/Web/FreshFarm.Web.Bff/appsettings.json`

Da xay dung:

- Dang ky options cho recommendation ML.
- Dang ky options cho session-aware recommendation.
- Dang ky client ket noi Ordering/Catalog.
- Dang ky service metrics, session signal, reranker, rollout, tuning.
- Dang ky background service refresh affinity.
- Dang ky background service refresh ML.
- Ho tro Redis/distributed cache cho session signal.
- Co health check lien quan Redis/session cache.

Gia tri cua phan nay:

- Recommendation khong chi la ham tinh diem, ma da duoc gan vao runtime.
- Co co che chay nen va cau hinh qua appsettings.

## 13. Kiem thu da co trong source

Phan test nam o:

- `src/Tests/FreshFarm.Ordering.Api.Tests/RecommendationEventsControllerTests.cs`
- `src/Tests/FreshFarm.Ordering.Api.Tests/RecommendationMetricsServiceTests.cs`
- `src/Tests/FreshFarm.Ordering.Api.Tests/RecommendationMlTrainingServiceTests.cs`
- `src/Tests/FreshFarm.Ordering.Api.Tests/RecommendationAffinityRefreshSignalTests.cs`
- `src/Tests/FreshFarm.Web.Bff.Tests/BffRecommendationEventsControllerTests.cs`
- `src/Tests/FreshFarm.Web.Bff.Tests/SessionAwareRecommendationRerankerTests.cs`
- `src/Tests/FreshFarm.Web.Bff.Tests/SessionSignalServiceTests.cs`
- `src/Tests/FreshFarm.Web.Bff.Tests/RecommendationExperimentServiceTests.cs`
- `src/Tests/FreshFarm.Web.Bff.Tests/MultiObjectiveRecommendationRolloutServiceTests.cs`
- `src/Tests/FreshFarm.Web.Bff.Tests/SessionAwareRecommendationWeightTuningServiceTests.cs`

Y nghia:

- Test controller event de dam bao payload hop le duoc forward/luu dung.
- Test metrics de dam bao CTR/objective/negative feedback tinh dung.
- Test ML training de dam bao co the skip khi du lieu thieu va materialize khi du lieu du.
- Test reranker de dam bao session signal co anh huong dung len thu tu goi y.
- Test experiment de dam bao chia nhom A/B on dinh.
- Test rollout/tuning de dam bao guardrail va rollback hoat dong.

## 14. Tom tat ngan de dua vao bao cao

Trong phan goi y, du an da duoc nang cap tu hien thi san pham chung sang mot he thong recommendation co nhieu lop:

1. Thu thap hanh vi nguoi dung qua view, search, click, impression.
2. Tinh truoc cac bang affinity va preference trong Ordering.
3. Bo sung ML.NET matrix factorization cho diem user-product.
4. BFF ket hop ML, collaborative, content-based va business rules thanh hybrid ranking.
5. Trang chu, search va product detail deu co goi y rieng.
6. Session-aware rerank dung Redis de phan ung theo hanh vi gan day.
7. A/B testing, CTR, add-to-cart, purchase va revenue metrics duoc track.
8. Co rollout/tuning tu dong de tang traffic an toan va rollback khi metric xau.

Ket qua la phan goi y khong con la danh sach tinh, ma tro thanh mot pipeline gom: thu thap du lieu -> tinh diem -> phuc vu goi y -> do luong -> tuning/rollout.

