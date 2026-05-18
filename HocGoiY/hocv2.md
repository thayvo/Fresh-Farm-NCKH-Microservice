# Brief Tu Chua Cho AI Nen Web Ve Du An FreshFarm - Phan Goi Y San Pham

Tai lieu nay duoc viet de dua truc tiep cho mot AI nen web khong co quyen truy cap vao source code FreshFarm. AI do can doc file nay nhu mot "project briefing" day du, khong duoc tu doan theo ly thuyet chung.

Muc tieu cua AI sau khi doc:

- Hieu FreshFarm la du an gi.
- Hieu module goi y san pham dang duoc thiet ke theo huong nao.
- Biet du lieu san pham hien co la bao nhieu, gom nhung vi du nao.
- Biet nhung thuat toan/tin hieu nao dang duoc dung trong source.
- Co the giup giai thich, viet slide, viet bao cao, tao vi du demo ve phan recommendation ma khong noi sai du an.

## 1. Tong quan du an FreshFarm

FreshFarm la he thong thuong mai dien tu cho nong san/thuc pham tuoi. Du an co cac nhom chuc nang lon:

- Nguoi mua xem danh muc, xem san pham, tim kiem, them gio hang, dat hang.
- Nguoi ban/nhan vien quan ly san pham, ton kho, don hang, giao hang.
- Quan tri vien theo doi nguoi dung, don hang, bao cao, rui ro, voucher/campaign.
- He thong co cac service tach biet theo kien truc ASP.NET Core/.NET.

Kien truc chinh co the hieu nhu sau:

- `Catalog service`: quan ly danh muc, san pham, anh, ton kho, thong tin san pham.
- `Ordering service`: quan ly gio hang, don hang, lich su mua, recommendation events, cac bang tinh diem goi y.
- `Identity service`: quan ly user, role, login, seller profile.
- `Web BFF`: lop web giao dien/Backend-for-Frontend, goi cac service phia sau de hien thi trang buyer/seller/admin.

Phan goi y san pham nam chu yeu o `Ordering service` va `Web BFF`, nhung phai lay du lieu san pham tu `Catalog service`.

## 2. Dieu rat quan trong: AI khong duoc noi chung chung

Khi tra loi ve module goi y cua FreshFarm, AI khong nen noi kieu:

> "He thong co the dung collaborative filtering hoac content-based filtering..."

Cach noi dung hon:

> "Trong FreshFarm, module goi y dang theo huong hybrid recommendation. Source co cac thanh phan thu thap event xem/tim/click, tinh product affinity, search keyword affinity, user-product score, basket affinity, replenishment profile, ML.NET matrix factorization va session-aware reranking."

FreshFarm khong phai chi co mot thuat toan. No la mot pipeline lai nhieu tin hieu.

## 3. So luong san pham hien tai

Theo seed database Catalog trong file `FreshFarmCatalogDb.sql`, bang `Products` hien co:

- Tong so dong san pham: 100.
- San pham co `Status = 1`: 30 san pham.
- San pham co `Status = 0`: 70 san pham.
- ProductId nho nhat/lon nhat: 1 den 122.
- ProductId khong lien tuc, nen khong duoc nhin ProductId lon nhat la 122 roi ket luan co 122 san pham.

Nen dien dat:

> Du lieu seed hien co 100 dong san pham trong bang Products. Trong do 30 san pham co Status = 1, co the xem la dang bat/public theo logic hien tai; 70 san pham co Status = 0, dang tat/khong public.

## 4. Danh muc san pham hien co

Catalog co 7 danh muc chinh:

| CategoryId | Ten danh muc | Slug | So san pham trong seed | Vi du |
|---:|---|---|---:|---|
| 1 | Rau la | rau-la | 20 | Rau muong, Cai bo xoi, Rau lang, Cai thia |
| 2 | Rau an hoa / than / mam | rau-an-hoa-than-mam | 14 | Bong cai xanh, Sup lo trang, Mang tay xanh |
| 3 | Rau an qua | rau-an-qua | 14 | Ca chua bi, Dua leo mini, Bi do, Ca tim |
| 4 | Cu & re | cu-re | 11 | Ca rot, Khoai tay vang, Cu den |
| 5 | Nam | nam | 9 | Nam dui ga, Nam mo, Nam kim cham |
| 6 | Rau thom & gia vi | rau-thom | 9 | Hanh la, Ngo ri, Hung lui, Tia to |
| 7 | Trai cay | trai-cay | 23 | Tao Fuji Nhat, Cam Cara ruot do, Chuoi gia Viet Nam |

## 5. Mot so san pham that nen dung lam vi du

Dung cac san pham nay khi can tao vi du, slide, kich ban demo:

| ProductId | Ten san pham | Danh muc | Status | Ton kho | Ghi chu |
|---:|---|---|---:|---:|---|
| 1 | Rau muong | Rau la | 1 | 150 | San pham dang bat, de dung lam vi du rau pho bien |
| 6 | Bong cai xanh | Rau an hoa / than / mam | 0 | 175 | San pham rau, hien Status = 0 |
| 8 | Mang tay xanh | Rau an hoa / than / mam | 0 | 1122 | Ton kho cao, dung duoc cho vi du inventory/popularity |
| 13 | Ca rot | Cu & re | 0 | 204 | Vi du san pham hay mua kem voi rau/cu |
| 14 | Khoai tay vang | Cu & re | 0 | 165 | Vi du cu/re |
| 61 | Tao Fuji Nhat | Trai cay | 0 | 128 | Vi du trai cay |
| 62 | Cam Cara ruot do | Trai cay | 0 | 136 | Nen dung thay cho "Cam sanh" neu can vi du ve cam/citrus |
| 68 | Chuoi gia Nam My | Trai cay | 0 | 191 | Vi du chuoi trong nhom trai cay |
| 116 | Chuoi gia Viet Nam | Trai cay | 1 | 2 | San pham dang bat, ton kho thap |
| 117 | Oi xa li | Trai cay | 1 | 150 | San pham trai cay dang bat |
| 118 | Dua hau khong hat mini | Trai cay | 1 | 80 | San pham trai cay dang bat |
| 119 | Thanh long ruot trang | Trai cay | 1 | 118 | San pham trai cay dang bat |
| 120 | Sau rieng Ri6 | Trai cay | 1 | 26 | San pham trai cay gia cao |

Luu y dac biet:

- Trong seed hien tai khong thay san pham ten "Cam sanh".
- Neu slide/hoi thoai cu dung "Cam sanh" thi nen sua thanh "Cam Cara ruot do" de bam sat du lieu that.
- Neu van muon dung "Cam sanh" thi phai ghi ro do la vi du minh hoa, khong phai san pham da xac nhan trong seed.

## 6. Du lieu nao duoc module goi y su dung?

FreshFarm co cac nguon tin hieu sau:

1. Du lieu san pham:
   - Ten san pham.
   - Danh muc.
   - Mo ta ngan/dai.
   - Gia.
   - Ton kho.
   - Trang thai san pham.
   - Anh san pham.
   - Seasonality/thuoc tinh neu co.

2. Du lieu hanh vi:
   - Xem san pham.
   - Tim kiem tu khoa.
   - Click ket qua tim kiem.
   - Recommendation impression: san pham da duoc hien trong khu vuc goi y.
   - Recommendation click: user click vao san pham goi y.
   - Mua hang thanh cong.
   - San pham mua cung don.
   - San pham xem/click cung session.

3. Du lieu ca nhan hoa:
   - User-product score.
   - User-category score.
   - User-seller score.
   - San pham user/session da quan tam.
   - San pham ung vien tu collaborative.
   - Negative feedback neu co.

4. Du lieu phien hien tai:
   - Tu khoa tim gan day.
   - San pham click gan day.
   - Seller click gan day.
   - Category click gan day.

## 7. Cac bang/model recommendation quan trong

Trong Ordering service co cac bang/model sau. AI can hieu y nghia, khong can thay source moi giai thich duoc:

| Bang/model | Y nghia |
|---|---|
| `ProductViewEvent` | Ghi nhan user/session xem mot san pham |
| `SearchEvent` | Ghi nhan user/session tim kiem mot keyword |
| `SearchClickEvent` | Ghi nhan user click vao san pham sau khi tim kiem |
| `RecommendationImpressionEvent` | Ghi nhan san pham goi y da duoc hien thi |
| `RecommendationClickEvent` | Ghi nhan user click vao san pham trong khu vuc goi y |
| `RecommendationProductAffinity` | Quan he seed product -> candidate product dua tren co-purchase/co-click/co-view |
| `RecommendationSearchKeywordAffinity` | Quan he keyword -> product |
| `RecommendationUserProductScore` | Diem user co kha nang thich mot san pham |
| `RecommendationUserCategoryScore` | Diem user thich mot danh muc |
| `RecommendationUserSellerScore` | Diem user thich mot seller |
| `RecommendationBasketAffinity` | San pham hay di cung trong gio hang/don hang |
| `RecommendationReplenishmentProfile` | Ho so mua lai theo chu ky cua user-product |
| `RecommendationHomePreferenceSeed` | San pham user/session da quan tam, lam seed cho trang chu |
| `RecommendationHomeCollaborativeCandidate` | Ung vien goi y collaborative cho trang chu |

## 8. Cac API event recommendation

Route goc cua API event:

```text
api/orders/recommendation-events
```

Endpoint chinh:

| Endpoint | Cong dung |
|---|---|
| `POST product-view` | Luu su kien user/session xem san pham |
| `POST search` | Luu keyword tim kiem |
| `POST search-click` | Luu click vao san pham sau tim kiem |
| `POST recommendation-impression` | Luu viec san pham duoc hien thi trong block goi y |
| `POST recommendation-click` | Luu viec user click vao san pham goi y |

Y nghia:

> Recommendation cua FreshFarm khong chi dua vao san pham co san. He thong truoc tien thu thap event. Sau do job/service tinh lai cac bang diem nhu product affinity, keyword affinity, user-product score, basket affinity, replenishment profile.

## 9. Thuat toan 1 - Popularity / rule-based baseline

Y tuong:

Khi user moi vao FreshFarm va chua co lich su, he thong co the hien san pham pho bien, san pham moi, san pham ban chay, san pham con hang, san pham dang uu tien kinh doanh.

Trong FreshFarm co cac nhom algorithm/source cho trang chu:

- `global_trending_v1`: san pham xu huong.
- `catalog_new_arrivals_v1`: san pham moi.
- `global_best_sellers_v1`: san pham ban chay.
- `ordering_sold_count_v1`: dua vao so luong da ban tu Ordering.

Vi du dung trong thuyet trinh:

> Neu user moi vao FreshFarm, he thong chua biet user thich gi. FreshFarm co the uu tien nhung san pham dang bat va phu hop nhu Rau muong, Oi xa li, Thanh long ruot trang, Dua hau khong hat mini, hoac cac san pham ban chay theo du lieu don hang.

Diem manh:

- De trien khai.
- Tot cho cold start user moi.
- De giai thich voi hoi dong/thay co.

Diem yeu:

- Chua ca nhan hoa sau.
- User nao cung thay gan giong nhau neu chi dung popularity.

## 10. Thuat toan 2 - Content-based filtering

Y tuong:

Content-based filtering goi y san pham tuong tu san pham user da xem/thich. Tin hieu noi dung co the gom:

- Ten san pham.
- Danh muc.
- Mo ta.
- Gia.
- Seller.
- Thuoc tinh.
- Seasonality.

Vi du dung du lieu that:

```text
User xem: Cam Cara ruot do ProductId 62
He thong hieu day la trai cay/citrus.
He thong co the goi y them:
- Tao Fuji Nhat
- Chuoi gia Nam My
- Oi xa li
- Thanh long ruot trang
- Dua MD2 Thai Lan
```

Noi trong slide:

> Content-based filtering cua FreshFarm co the dua vao noi dung san pham va danh muc. Neu user quan tam den Cam Cara ruot do, he thong co the uu tien cac san pham trai cay tuong tu.

Can than:

- Dung "co the" neu chua co query runtime.
- Khong noi chac chan he thong se goi y dung danh sach tren, vi danh sach cuoi con phu thuoc status, ton kho, score, seller diversity va cac filter khac.

## 11. Thuat toan 3 - Collaborative filtering / product affinity

Y tuong:

Collaborative filtering hoc tu hanh vi cua nhieu user/session:

- San pham nao hay mua cung nhau?
- San pham nao hay click cung session?
- San pham nao hay xem cung session?

Trong FreshFarm, product affinity co cong thuc diem:

```text
AffinityScore =
  CoPurchaseOrderCount * 32
+ CoClickSessionCount * 14
+ CoViewSessionCount * 6
```

Y nghia cong thuc:

- Mua cung don hang la tin hieu manh nhat, nen nhan 32.
- Click cung session la tin hieu trung binh, nhan 14.
- Xem cung session yeu hon, nhan 6.

Vi du:

```text
Nhieu user/session co hanh vi:
- Xem Rau muong roi xem Bong cai xanh
- Mua Rau muong cung Ca rot
- Click Chuoi gia Viet Nam va Oi xa li trong cung session

He thong co the hoc cac cap san pham lien quan.
```

Noi trong slide:

> Collaborative/product affinity trong FreshFarm khong chi dua vao danh muc. Neu nhieu user mua, xem hoac click hai san pham cung nhau, he thong co the goi y san pham con lai khi user quan tam mot san pham.

## 12. Thuat toan 4 - ML.NET matrix factorization

FreshFarm co service train ML.NET matrix factorization.

Y tuong:

Tao ma tran user-product, trong do moi dong la user, moi cot la product. Tu lich su view/click/purchase, model du doan user co kha nang thich san pham nao.

Du lieu dau vao:

- View count.
- Search click count.
- Recommendation click count.
- Purchase count.
- Last interacted time.
- Negative feedback neu co.

Source dung:

- `MatrixFactorizationTrainer`.
- Loss function: `SquareLossOneClass`.
- Output co the materialize vao `RecommendationUserProductScore`.

Truong hop service co the khong train:

- Qua it interaction.
- Qua it user.
- Qua it product.
- Option ML recommendation bi tat va khong force rebuild.

Noi trong slide:

> ML.NET matrix factorization giup FreshFarm du doan user co kha nang thich san pham nao dua tren hanh vi user-product. Neu du lieu chua du, he thong co the tam bo qua model ML va dung cac tin hieu hybrid khac.

## 13. Thuat toan 5 - Hybrid recommendation trang chu

Day la y quan trong nhat.

Trang chu recommendation cua FreshFarm khong chi lay mot nguon. No tron 3 nguon ung vien lon:

| Nguon ung vien | Ten trong source | Weight |
|---|---|---:|
| ML.NET user-product score | `ML` | 1.0 |
| Collaborative candidate | `Collaborative` | 0.8 |
| Content candidate | `Content` | 0.6 |

Sau do Web BFF:

- Tron ung vien theo thu tu interleaving.
- Lay thong tin san pham tu Catalog.
- Loai/han che san pham khong hop le.
- Ap dung diversity de tranh lap qua nhieu cung seller/category.
- Ap dung session-aware rerank neu user co hanh vi gan day.
- Tra ve danh sach goi y cho block home.

Endpoint trang chu:

```text
GET recommendations/home
```

Algorithm label:

- Neu chi content: `content_based_home_v1`.
- Neu co ket hop nhieu tin hieu: `hybrid_home_v1`.

Noi trong slide:

> FreshFarm dung hybrid recommendation cho trang chu. He thong lay ung vien tu ML, collaborative va content-based, sau do tron diem, da dang hoa va rerank theo hanh vi trong phien hien tai.

## 14. Thuat toan 6 - Search keyword affinity

Y tuong:

Search keyword affinity hoc quan he giua tu khoa tim kiem va san pham user thuc su click/xem/mua sau do.

Vi du:

```text
Nhieu user tim: "chuoi"
Sau do click: Chuoi gia Viet Nam ProductId 116

He thong hoc:
keyword "chuoi" co lien quan manh den ProductId 116.
```

Ung dung:

- Cai thien thu tu ket qua tim kiem.
- Day san pham phu hop keyword len cao.
- Ket hop voi recommendation o trang chu neu user vua tim kiem.

Noi trong slide:

> Search keyword affinity giup FreshFarm hoc tu hanh vi tim kiem that: user tim tu khoa nao va sau do chon san pham nao.

## 15. Thuat toan 7 - Basket affinity

Y tuong:

Basket affinity goi y san pham hay mua cung nhau. No phu hop voi grocery/fresh food vi user thuong mua theo bo mon/bua an.

Vi du minh hoa:

```text
User co Rau muong trong gio.
He thong co the goi y Ca rot, Khoai tay vang, Bong cai xanh hoac san pham rau/cu khac neu du lieu don hang cho thay hay mua kem.
```

Can than:

- Neu chua query bang `RecommendationBasketAffinity` runtime, khong duoc noi chac cap nao da duoc tinh ra.
- Nen noi "co the goi y" hoac "neu du lieu co-purchase ung ho".

Noi trong slide:

> Basket affinity cua FreshFarm dung de goi y san pham bo sung khi user dang mua sam, vi nhieu san pham thuc pham thuong duoc mua kem nhau.

## 16. Thuat toan 8 - Replenishment recommendation

Y tuong:

Replenishment recommendation du doan khi nao user can mua lai mot san pham. Rat hop voi thuc pham tuoi/nhu yeu pham.

Vi du:

```text
User thuong mua Rau muong moi 5-7 ngay.
Lan mua gan nhat da 6 ngay.
He thong tang diem goi y Rau muong vi user co kha nang can mua lai.
```

Bang/model lien quan:

- `RecommendationReplenishmentProfile`.

Noi trong slide:

> Replenishment recommendation giup FreshFarm goi y san pham mua lap lai theo chu ky, vi grocery co nhieu mat hang nguoi dung mua dinh ky.

## 17. Thuat toan 9 - Session-aware reranking

Y tuong:

Sau khi da co danh sach goi y, session-aware reranker sap xep lai dua tren hanh vi hien tai cua user.

Tin hieu phien hien tai:

- Recent searches.
- Recent clicks.
- Clicked product IDs.
- Clicked seller IDs.
- Clicked categories.
- Negative feedback.
- Objective scores.

Vi du:

```text
Lich su dai han cua user thich trai cay.
Nhung trong phien hien tai user tim "rau" va click Rau muong.

He thong co the day cac san pham rau/cung category len cao hon,
du trang chu ban dau dang co nhieu trai cay.
```

Noi trong slide:

> Session-aware reranking giup FreshFarm phan ung voi y dinh hien tai cua user, khong chi dua vao lich su dai han.

## 18. Thuat toan 10 - Evaluation va A/B testing

Day khong phai thuat toan sinh goi y, nhung la cach biet goi y co tot khong.

Metric nen noi:

- Click-through rate: user co click san pham goi y khong?
- Add-to-cart rate: user co them san pham goi y vao gio khong?
- Conversion rate: goi y co dan den mua hang khong?
- Average order value: gia tri don hang co tang khong?
- Repeat purchase rate: user co quay lai mua tiep khong?
- Latency: goi y co nhanh khong?
- Diversity: danh sach co da dang category/seller khong?

A/B testing:

```text
Group A: logic goi y cu
Group B: hybrid recommendation moi

So sanh CTR, add-to-cart, conversion, doanh thu, latency.
```

Noi trong slide:

> FreshFarm can danh gia recommendation bang metric hanh vi va kinh doanh, khong chi bang cam giac danh sach co ve hop ly.

## 19. Kich ban demo de AI dung khi giai thich

Dung scenario nay vi du lieu bam vao san pham that:

```text
Nguoi dung moi:
- Vao trang chu FreshFarm lan dau.
- Chua co lich su view/click/mua.
- He thong dung popularity/new arrivals/best sellers lam baseline.

Nguoi dung xem Cam Cara ruot do:
- Content-based nhan ra user dang quan tam trai cay/citrus.
- He thong co the goi y cac trai cay khac nhu Tao Fuji Nhat, Oi xa li, Thanh long ruot trang.

Nguoi dung tim "chuoi":
- SearchEvent luu keyword "chuoi".
- Neu user click Chuoi gia Viet Nam, SearchClickEvent duoc ghi.
- Neu nhieu user cung hanh vi nay, RecommendationSearchKeywordAffinity tang diem cho Chuoi gia Viet Nam.

Nguoi dung them Rau muong vao gio:
- Basket affinity/co-purchase co the goi y san pham hay mua kem.

Nguoi dung thuong mua Rau muong moi 7 ngay:
- RecommendationReplenishmentProfile co the day Rau muong len khi gan den chu ky mua lai.

Trong phien hien tai user click nhieu san pham rau:
- SessionAwareRecommendationReranker day san pham rau/cung category len cao hon.
```

## 20. Cau tra loi mau ngan gon cho AI khac

Neu duoc hoi "FreshFarm dung thuat toan goi y nao?", tra loi:

> FreshFarm dung hybrid recommendation. He thong ket hop popularity/rule-based baseline, content-based filtering, collaborative/product affinity, ML.NET matrix factorization, search keyword affinity, basket affinity, replenishment va session-aware reranking. Trang chu lay ung vien tu ML, Collaborative va Content voi weight lan luot 1.0, 0.8, 0.6, sau do da dang hoa va rerank theo hanh vi phien hien tai.

Neu duoc hoi "Du an co bao nhieu san pham?", tra loi:

> Theo seed Catalog, bang Products co 100 dong san pham. Trong do 30 san pham co Status = 1 va 70 san pham co Status = 0. ProductId lon nhat la 122 nhung ID khong lien tuc, nen khong ket luan la co 122 san pham.

Neu duoc hoi "Cho vi du san pham", tra loi:

> Co the dung cac san pham that nhu Rau muong ProductId 1, Cam Cara ruot do ProductId 62, Chuoi gia Viet Nam ProductId 116, Oi xa li ProductId 117, Dua hau khong hat mini ProductId 118, Thanh long ruot trang ProductId 119, Sau rieng Ri6 ProductId 120.

Neu duoc hoi "Cam sanh co trong du an khong?", tra loi:

> Trong seed hien tai khong thay san pham ten Cam sanh. Neu can vi du ve cam/citrus, nen dung san pham that la Cam Cara ruot do ProductId 62. Neu dung Cam sanh thi chi nen xem la vi du minh hoa ngoai seed.

## 21. Nhung loi can tranh

- Khong noi FreshFarm chi dung collaborative filtering.
- Khong noi FreshFarm chi dung content-based filtering.
- Khong noi co 122 san pham chi vi ProductId lon nhat la 122.
- Khong bo qua `Status = 1` va `Status = 0`.
- Khong khang dinh "Cam sanh" la san pham trong seed.
- Khong khang dinh cap basket affinity cu the neu khong co du lieu runtime.
- Khong giai thich ML.NET matrix factorization nhu deep learning.
- Khong viet slide qua nhieu cong thuc toan neu nguoi nghe la nguoi moi; nen giai thich bang hanh vi view/search/click/purchase.

## 22. Danh sach file source tham khao neu sau nay co quyen truy cap repo

Neu AI/nguoi doc sau nay co the mo repo, hay doc cac file nay:

- `src/Services/Catalog/FreshFarm.Catalog.Api/Models/FreshFarmCatalogDBContext.cs`
- `src/Services/Catalog/FreshFarm.Catalog.Api/Models/Product.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Models/FreshFarmOrderingDBContext.RecommendationEvents.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationEventsController.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlTrainingService.cs`
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs`
- `src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs`
- `src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationReranker.cs`

## 23. Ban tom tat 1 phut

FreshFarm la he thong ban nong san/thuc pham tuoi. Phan goi y san pham duoc thiet ke theo huong hybrid recommendation, ket hop nhieu tin hieu: san pham pho bien, noi dung san pham, hanh vi user/session, san pham mua/xem/click cung nhau, tu khoa tim kiem, gio hang, chu ky mua lai, ML.NET matrix factorization va rerank theo hanh vi phien hien tai. Du lieu seed Catalog hien co 100 dong san pham, trong do 30 san pham co Status = 1. Vi du san pham that nen dung la Rau muong, Cam Cara ruot do, Chuoi gia Viet Nam, Oi xa li, Thanh long ruot trang. Neu can vi du "cam", nen dung Cam Cara ruot do thay vi Cam sanh vi seed hien tai khong thay Cam sanh.

## 24. Nguyen li van hanh tong the cua he thong goi y

Co the hieu FreshFarm recommendation theo 6 buoc:

### Buoc 1: Catalog cung cap tap san pham hop le

Catalog service quan ly bang `Products`. Moi san pham co cac truong quan trong nhu:

- `ProductID`
- `CategoryId`
- `ProductName`
- `Sku`
- `Price`
- `Status`
- `StockQuantity`
- `ImageFileName`
- `CreatedDate`
- `ShortDescription`
- `LongDescription`
- `UnitID`
- `NearExpiryDays`
- `IsManuallyDisabled`

Khi Web BFF can hien recommendation, no khong chi lay diem tu Ordering ma phai hydrate lai san pham tu Catalog de biet ten, anh, gia, ton kho, category, seller, rating, sold count.

### Buoc 2: Ordering thu thap event hanh vi

Khi user xem/tim/click/mua, Ordering service luu event:

- Xem san pham -> `ProductViewEvent`.
- Tim kiem -> `SearchEvent`.
- Click ket qua tim kiem -> `SearchClickEvent`.
- San pham goi y duoc hien thi -> `RecommendationImpressionEvent`.
- User click san pham goi y -> `RecommendationClickEvent`.
- Don hang thanh cong -> `Orders` + `OrderDetails`.

Nhung event nay la "nguyen lieu" de tinh diem. Khong co event thi cac bang affinity/ML/replenishment se ngheo du lieu.

### Buoc 3: RecommendationAffinityService rebuild cac bang diem

Service `RecommendationAffinityService` lay event trong cac lookback window:

- Product affinity: nhin lai 180 ngay.
- Home preference: nhin lai 90 ngay.
- Replenishment: nhin lai 365 ngay.
- Negative feedback: nhin lai 30 ngay.

Sau do service xoa cac bang diem cu va materialize lai:

- `RecommendationProductAffinity`
- `RecommendationSearchKeywordAffinity`
- `RecommendationUserProductScore`
- `RecommendationUserCategoryScore`
- `RecommendationUserSellerScore`
- `RecommendationBasketAffinity`
- `RecommendationReplenishmentProfile`
- `RecommendationHomePreferenceSeed`
- `RecommendationHomeCollaborativeCandidate`

### Buoc 4: ML.NET co the rebuild user-product score

`RecommendationMlTrainingService` build dataset user-product tu view/search click/recommendation click/purchase/negative feedback. Neu du du lieu, no train Matrix Factorization va materialize ket qua vao `RecommendationUserProductScore`.

Neu khong du du lieu, service bo qua voi ly do nhu `insufficient_interaction_data`.

### Buoc 5: Web BFF tron ung vien recommendation

`BffCatalogController` lay ung vien tu:

- ML user-product score.
- Collaborative candidate.
- Content candidate.

Sau do ap dung weight:

```text
ML = 1.0
Collaborative = 0.8
Content = 0.6
```

Roi hydrate san pham tu Catalog, sap xep, da dang hoa seller/category/region va tra ve JSON cho frontend.

### Buoc 6: Session-aware reranker sap xep lai theo phien hien tai

Neu user trong phien hien tai vua search/click san pham/category/seller nao, `SessionAwareRecommendationReranker` se boost cac san pham khop intent hien tai. Vi du user lau nay hay mua trai cay, nhung hom nay search "rau" va click Rau muong thi danh sach co the uu tien rau hon.

## 25. Cach tinh diem chi tiet trong he thong

Day la cac cong thuc da thay trong source. Khi AI giai thich, nen noi day la "logic hien tai trong source", khong phai cong thuc ly thuyet tu ben ngoai.

### 25.1. Product affinity score

Dung de tinh san pham nao lien quan san pham nao.

```text
AffinityScore =
  CoPurchaseOrderCount * 32
+ CoClickSessionCount * 14
+ CoViewSessionCount * 6
```

Y nghia:

- Mua cung don hang co trong so cao nhat: 32.
- Click cung session: 14.
- Xem cung session: 6.

Vi du minh hoa:

```text
Rau muong -> Ca rot
CoPurchaseOrderCount = 3
CoClickSessionCount = 2
CoViewSessionCount = 5

AffinityScore = 3*32 + 2*14 + 5*6 = 154
```

San pham candidate co score cao hon se duoc uu tien hon.

### 25.2. Search keyword affinity score

Dung de hoc tu khoa nao lien quan san pham nao.

```text
HybridSearchScore =
  SearchClickCount * 20
+ SearchRecommendationClickCount * 14
+ SearchViewSessionCount * 6
```

Y nghia:

- User click san pham sau khi search la tin hieu manh nhat: 20.
- User click san pham trong recommendation sau search: 14.
- User xem san pham trong session co keyword: 6.

Vi du:

```text
Keyword = "chuoi"
Product = Chuoi gia Viet Nam
SearchClickCount = 10
SearchRecommendationClickCount = 3
SearchViewSessionCount = 8

HybridSearchScore = 10*20 + 3*14 + 8*6 = 290
```

### 25.3. Home preference seed score

Dung de xac dinh san pham nao user/session da the hien quan tam, lam seed cho goi y trang chu.

```text
PreferenceScore =
  ViewCount * 10
+ SearchClickCount * 28
+ RecommendationClickCount * 22
+ PurchaseCount * 35
+ NegativeFeedbackPenalty * 10
```

Y nghia:

- Purchase la manh nhat: 35.
- Search click: 28.
- Recommendation click: 22.
- View: 10.
- Negative feedback co the tru diem vi penalty score thuong la so am.

### 25.4. Home collaborative candidate score

FreshFarm lay preference seed cua user/session, roi lan sang cac candidate qua product affinity.

Seed co score cang cao thi anh huong cang manh:

```text
SeedWeight = 1 + min(PreferenceScore, 240) / 120
CollaborativeScore += AffinityScore * SeedWeight
CollaborativeScore += NegativeFeedbackPenalty * 10
```

Vi du:

```text
User quan tam Rau muong, PreferenceScore = 120
SeedWeight = 1 + 120/120 = 2
Rau muong -> Ca rot co AffinityScore = 154

CollaborativeScore cho Ca rot = 154 * 2 = 308
```

### 25.5. User-product score heuristic

Ngoai ML.NET, source cung co logic tinh diem user-product tu hanh vi:

```text
UserProductScore =
  PurchaseCount * 42
+ SearchClickCount * 22
+ RecommendationClickCount * 18
+ ViewCount * 8
+ RecencyBoost
+ NegativeFeedbackPenalty * 10
```

Recency boost:

```text
Neu co LastInteractedAtUtc:
RecencyBoost = max(0, 18 - min(ageDays, 18))
```

Y nghia:

- Mua hang la tin hieu manh nhat: 42.
- Search click: 22.
- Recommendation click: 18.
- View: 8.
- Tuong tac cang moi thi cong them toi da 18 diem.

### 25.6. User-seller score

Dung de biet user hay tuong tac voi seller nao.

```text
UserSellerScore =
  PurchaseCount * 42
+ SearchClickCount * 14
+ ViewCount * 4
```

Y nghia:

- Mua hang tu seller la tin hieu manh.
- Click sau search voi seller do cung co gia tri.
- View seller/product cua seller do la tin hieu yeu hon.

### 25.7. User-category score

Dung de biet user thich danh muc nao.

FreshFarm cong don tu cac `UserProductScore` theo category, sau do cong them long-term lift:

```text
UserCategoryScore += UserProductScore + LongTermCategoryLift
```

Trong do:

```text
LongTermCategoryLift =
  PurchaseCount * 8
+ SearchClickCount * 4
+ RecommendationClickCount * 3
+ ViewCount * 1.5
+ RecencyLift
```

RecencyLift:

```text
Neu co LastInteractedAtUtc:
RecencyLift = max(0, 12 - min(ageDays, 12))
```

### 25.8. Basket affinity score

Dung cho goi y san pham hay mua kem.

```text
BasketScore =
  CoPurchaseOrderCount * 30
+ CoClickSessionCount * 6
```

Chi cac product affinity co `CoPurchaseOrderCount > 0` moi duoc lay lam basket affinity.

### 25.9. Replenishment score

Dung cho san pham mua lap lai theo chu ky.

He thong tinh:

- Tong so luong da mua (`PurchaseCount`).
- Lan mua gan nhat (`LastPurchasedAtUtc`).
- Khoang cach trung binh giua cac lan mua (`AverageRepurchaseDays`).
- Ngay du kien mua lai (`ExpectedReorderAtUtc`).

Cong thuc:

```text
TimingScore =
  Neu co ExpectedReorderAtUtc:
    max(0, 30 - abs(ExpectedReorderAtUtc - ComputedAt).TotalDays)
  Neu chua co chu ky:
    max(0, 14 - min(daysSinceLastPurchase, 14))

ReplenishmentScore =
  PurchaseCount * 14
+ TimingScore
+ max(0, 18 - min(AverageRepurchaseDays, 18)) neu co AverageRepurchaseDays
```

Y nghia:

- Mua nhieu lan thi score cao.
- Gan ngay du kien mua lai thi timing score cao.
- Chu ky mua lai ngan/ro rang duoc cong them.

### 25.10. ML.NET label va normalized score

ML.NET build label tu interaction co decay theo thoi gian. Half-life:

```text
PositiveSignalHalfLifeDays = 30
decay = 0.5 ^ (ageDays / 30)
```

Cac signal view/search click/recommendation click/purchase deu cong vao label theo trong so cau hinh trong `RecommendationMlOptions`. Negative feedback co the giam label, nhung label yeu nhat duoc giu o:

```text
WeakNegativeLabel = 0.15
```

Sau khi model predict, score duoc normalize:

```text
Neu maxScore > minScore:
Normalized = floor + ((score - minScore) / (maxScore - minScore) * (ceiling - floor))

Neu cac score bang nhau:
Dung rankScore dua theo thu hang trong top N.
```

### 25.11. Home recommendation final score

Khi Web BFF da co san pham candidate, score trang chu duoc tinh theo y tuong:

```text
Score =
  BaseContentScore
+ PersonalizationMatchScore
+ CollaborativeScore
+ UserProductScore
+ LongTermUserProductBoost
+ LongTermSellerBoost
+ RecentSellerBoost
+ LongTermPreferenceProfileBoost
```

Sau do sort:

```text
OrderBy Score desc
ThenBy CollaborativeScore desc
ThenBy UserProductScore desc
ThenBy MatchScore desc
ThenBy AverageRating desc
ThenBy SoldCount desc
ThenBy ProductId desc
```

### 25.12. Diversity penalty trong home candidate pool

De tranh danh sach toan cung mot seller hoac cung mot category, FreshFarm tinh penalty:

```text
rawPenaltyMultiplier =
  1 / (1 + SellerAlpha * sellerCount + CategoryBeta * categoryCount)

PenaltyMultiplier = max(MinMultiplier, rawPenaltyMultiplier)

SoftAdjustedScore = WeightedScore * PenaltyMultiplier
```

`SellerAlpha`, `CategoryBeta`, `MinMultiplier` lay tu `SessionAwareRecommendationOptions`.

### 25.13. Trending score

Neu source la `top_sold`:

```text
TrendingScore = log10(SoldCount + 1) * 42
```

Neu source la `top_rated`:

```text
TrendingScore = AverageRating * 22 + log10(ReviewCount + 1) * 14
```

Neu source la `recent_popular`:

```text
RecentPopularScore =
  RecencyScore
+ log10(SoldCount + 1) * 18
+ AverageRating * 8
+ log10(ReviewCount + 1) * 6
```

Ton kho duoc cong/tru:

```text
AvailableStock > 20: +10
AvailableStock > 0: +6
AvailableStock = 0: -20
```

### 25.14. New arrival score

San pham moi duoc xep theo `CreatedDate` giam dan truoc, roi tinh score:

```text
ageDays = DateTime.UtcNow - CreatedDate
recencyScore = max(0, 30 - min(ageDays, 30))

NewArrivalScore =
  recencyScore
+ log10(AvailableStock + 1) * 8
+ AverageRating * 4
+ log10(SoldCount + 1) * 4
```

Nghia la san pham tao trong 30 ngay gan day co recency score. Neu qua 30 ngay thi recency score = 0, nhung van co the co diem tu ton kho, rating, sold count.

### 25.15. Best seller score

Truoc tien tinh:

```text
BestSellerRankingScore =
  RecentSoldCount * 0.7
+ SoldCount * 0.3
```

Sau do:

```text
BestSellerScore =
  BestSellerRankingScore * 10
+ AverageRating * 6
+ log10(ReviewCount + 1) * 6
+ StockBoost
```

StockBoost:

```text
AvailableStock > 20: +8
AvailableStock > 0: +4
AvailableStock = 0: +0
```

## 26. San pham cu va san pham moi trong seed hien tai

FreshFarm khong co cot ten la "old/new product". He thong phan biet san pham moi chu yeu bang `CreatedDate`.

### 26.1. Nhom san pham cu theo seed

Nhom san pham co CreatedDate som hon:

| CreatedDate | So san pham | Khoang ProductId | Vi du |
|---|---:|---|---|
| 2025-10-10T21:39:18.823 | 14 | 1-14 | Rau muong, Cai bo xoi, Rau lang, Cai thia |
| 2025-11-02T23:39:30.497 | 5 | 15-19 | Rau cai ngot, Cai ngong, Rau diep ca |
| 2025-11-02T23:39:30.500 | 10 | 20-29 | Bong cai trang, Rau mam dau Ha Lan, Hoa thien ly |
| 2025-11-03T10:00:00.000 | 31 | 30-60 | Xa lach xoan, Rau mong toi, Cai chip |
| 2025-11-03T23:00:00.000 | 18 | 61-78 | Tao Fuji Nhat, Cam Cara ruot do, Nho My khong hat |

Co the goi day la nhom san pham seed ban dau/cu hon vi CreatedDate nam o 2025-10 den dau 2025-11.

### 26.2. Nhom san pham moi hon theo seed

Nhom duoc them sau:

| CreatedDate | So san pham | Khoang ProductId | Vi du |
|---|---:|---|---|
| 2025-11-17T23:57:29.760 | 3 | 101-103 | Xa lach lo lo, Cai be xanh, Rau diep |
| 2025-11-17T23:57:29.763 | 17 | 104-120 | Bong cai xanh baby, Bong cai trang mini, Ca chua beef, Bi dao |
| 2026-02-03T11:07:30.883 | 1 | 121 | khoai mo |
| 2026-02-06T14:10:05.463 | 1 | 122 | Rau muong |

Luu y khi noi "san pham moi":

- Theo logic `products/new-arrivals`, FreshFarm sap xep theo `CreatedDate` moi nhat.
- Theo cong thuc score, recency boost chi co y nghia manh trong 30 ngay gan `DateTime.UtcNow`.
- Voi ngay hien tai cua phien lam viec la 2026-05-17, tat ca product seed tren deu qua 30 ngay, nen `recencyScore` trong `NewArrivalScore` se bang 0. Tuy vay endpoint new arrivals van co the lay cac product co `CreatedDate` moi nhat trong seed nhu ProductId 122, 121, 120... neu chung vuot qua filter ung vien hop le.

### 26.3. Khac nhau giua san pham cu va san pham moi trong recommendation

San pham moi:

- De duoc day trong section `products/new-arrivals`.
- Co recencyScore cao neu moi tao trong 30 ngay.
- Chua co nhieu view/click/purchase nen collaborative/ML co the yeu.
- Can popularity/content-based/business rule de tranh cold start product.

San pham cu:

- Co kha nang co nhieu event view/click/purchase hon.
- De co product affinity, basket affinity, user-product score, replenishment.
- Neu ban chay hoac duoc tuong tac nhieu thi co diem cao o trending/best sellers/hybrid.

Noi ngan gon:

> San pham moi trong FreshFarm duoc nhan dien chu yeu bang CreatedDate va section new arrivals. San pham cu co loi the ve lich su hanh vi, nen collaborative, ML, basket affinity va replenishment de hoc hon.

## 27. Goi code da copy sang HocGoiY/src

De dua cho AI nen web, thu muc `HocGoiY/src` da duoc copy cac file lien quan tu source chinh. AI co the doc cac file nay nhu evidence kem theo briefing.

### 27.1. Catalog service

```text
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Controllers/ProductsController.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Controllers/ProductOffersController.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/Category.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/FreshFarmCatalogDBContext.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/FreshFarmCatalogDBContext.Extras.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/Product.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/Product.Inventory.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/Product.SearchReadiness.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/ProductImage.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/ProductInfo.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Models/ProductSeasonality.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Dtos/ProductDto.cs
HocGoiY/src/Services/Catalog/FreshFarm.Catalog.Api/Dtos/ProductUpsertRequest.cs
```

### 27.2. Ordering recommendation controllers/dtos

```text
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/ProductInsightsController.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationEventsController.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMetricsController.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/RecommendationMlController.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Dtos/RecommendationMetricsDtos.cs
```

### 27.3. Ordering recommendation models

```text
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/FreshFarmOrderingDBContext.RecommendationEvents.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/ProductViewEvent.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/SearchEvent.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/SearchClickEvent.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationAddToCart.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationBasketAffinity.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationClick.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationClickEvent.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationHomeCollaborativeCandidate.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationHomePreferenceSeed.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationImpression.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationImpressionEvent.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationProductAffinity.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationPurchase.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationReplenishmentProfile.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationSearchKeywordAffinity.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationUserCategoryScore.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationUserProductScore.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Models/RecommendationUserSellerScore.cs
```

### 27.4. Ordering recommendation services/options

```text
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Options/RecommendationMlOptions.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/CatalogInventoryClient.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshBackgroundService.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityRefreshSignal.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationAffinityService.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsDao.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMetricsService.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlRefreshBackgroundService.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationMlTrainingService.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/RecommendationNegativeFeedbackScoring.cs
HocGoiY/src/Services/Ordering/FreshFarm.Ordering.Api/Services/SeasonalityScoring.cs
```

### 27.5. Web BFF recommendation

```text
HocGoiY/src/Web/FreshFarm.Web.Bff/Controllers/BffCatalogController.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Controllers/BffRecommendationEventsController.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Options/RecommendationExperimentOptions.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Options/SessionAwareRecommendationOptions.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Options/SessionAwareRecommendationTuningOptions.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Options/MultiObjectiveRecommendationRolloutOptions.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/IRecommendationExperimentService.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/IRecommendationMetricsClient.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/ISessionAwareRecommendationReranker.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/RecommendationMetricsClient.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationReranker.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationWeightTuningBackgroundService.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/SessionAwareRecommendationWeightTuningService.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/Services/SessionSignalService.cs
HocGoiY/src/Web/FreshFarm.Web.Bff/App_Data/session-aware-rerank-tuning.json
```

### 27.6. SQL seed/data kem theo

```text
HocGoiY/src/docs/FreshFarmCatalogDb/FreshFarmCatalogDb.sql
HocGoiY/src/docs/FreshFarmCatalogDb/FreshFarmCatalogDB.2026-03-29.recommendation-attribute-seed.delta.sql
HocGoiY/src/docs/FreshFarmCatalogDb/FreshFarmCatalogDB.2026-03-30.recommendation-multiseller-local-seed.sql
HocGoiY/src/docs/FreshFarmCatalogDb/FreshFarmCatalogDB.2026-04-25.product-seasonality.delta.sql
```

Tong so file da copy trong dot nay: 67 file.

