# 01. Tổng Quan Hệ Thống Gợi Ý

## 1. Hệ thống gợi ý là gì?

Hệ thống gợi ý là cơ chế chọn ra một tập sản phẩm phù hợp nhất với một người dùng tại một thời điểm.

Trong Fresh Farm:

- `user` là người mua
- `item` là sản phẩm nông sản
- `context` là ngữ cảnh:
  - đang ở trang chủ
  - đang tìm kiếm
  - đang xem một sản phẩm
  - là người mới hay người cũ

Nói đơn giản:

> Recommendation không phải là “hiện sản phẩm tốt nhất”, mà là “hiện sản phẩm phù hợp nhất trong đúng bối cảnh”.

## 2. Input và output của recommendation

### Input

1. Dữ liệu người dùng
   - đã xem gì
   - đã tìm gì
   - đã click gì
   - đã mua gì
2. Dữ liệu sản phẩm
   - danh mục
   - vùng trồng
   - mùa vụ
   - giá
   - seller
3. Dữ liệu ngữ cảnh
   - đang ở `home`, `search`, hay `similar`
   - người dùng mới hay cũ
4. Luật sản phẩm
   - còn hàng hay không
   - có giao nhanh không
   - có cần đa dạng seller/origin không

### Output

Output không chỉ là danh sách item.

Trong Fresh Farm còn có:

- điểm recommendation
- lý do recommendation
- tag hiển thị
- section hiển thị
- metadata về nguồn signal

## 3. Công thức trực giác cơ bản

\[
Score(u, i) = w_1 \cdot Relevance(u, i) + w_2 \cdot Quality(i) + w_3 \cdot Context(u, i)
\]

Trong đó:

- \(u\): user
- \(i\): item
- `Relevance`: item có hợp gu user không
- `Quality`: item có tốt không
- `Context`: item có hợp hoàn cảnh hiện tại không

## 4. Ví dụ số rất đơn giản

Giả sử user A:

- vừa xem `rau muống`
- hay mua đồ từ Long An

Ba item:

1. Rau muống Long An
2. Cải ngọt Đà Lạt
3. Táo nhập khẩu

Cho điểm giả sử:

- `Relevance`
  - item 1 = 9
  - item 2 = 7
  - item 3 = 2
- `Quality`
  - item 1 = 7
  - item 2 = 8
  - item 3 = 9
- `Context`
  - item 1 = 8
  - item 2 = 5
  - item 3 = 3

Nếu dùng:

\[
Score = 0.5 \cdot Relevance + 0.3 \cdot Quality + 0.2 \cdot Context
\]

Ta có:

- Item 1:
  \[
  0.5 \cdot 9 + 0.3 \cdot 7 + 0.2 \cdot 8 = 8.2
  \]
- Item 2:
  \[
  0.5 \cdot 7 + 0.3 \cdot 8 + 0.2 \cdot 5 = 6.9
  \]
- Item 3:
  \[
  0.5 \cdot 2 + 0.3 \cdot 9 + 0.2 \cdot 3 = 4.3
  \]

Kết luận:

- item 1 nên đứng đầu

## 5. Ba họ thuật toán lớn

### Content-based

Gợi ý item giống với thứ user từng thích dựa trên thuộc tính của item.

### Collaborative filtering

Gợi ý dựa trên hành vi của người dùng giống bạn hoặc item thường đi cùng nhau.

### Hybrid

Trộn nhiều nguồn tín hiệu với luật nghiệp vụ.

Fresh Farm hiện đang đi theo hướng này.

## 6. Tại sao production khó hơn ví dụ sách giáo khoa?

Vì hệ thống thật phải xử lý:

- người mới chưa có lịch sử
- sản phẩm mới
- top-k bị lặp một seller
- top-k quá giống nhau
- giải thích cho user hiểu
- thời gian phản hồi phải nhanh

## 7. Ánh xạ vào Fresh Farm

- `Ordering` tích lũy tín hiệu:
  - view
  - search
  - click
  - purchase
- `Ordering` materialize thành:
  - user-product score
  - user-category score
  - user-seller score
  - basket affinity
  - replenishment profile
- `BFF` lấy các tín hiệu đó để:
  - tính điểm
  - rerank
  - ép diversity
  - chia section

## 8. Điều cần nhớ nhất

Một hệ thống gợi ý tốt phải cân bằng:

1. Phù hợp với user
2. Có ích thực tế
3. Đủ đa dạng để khám phá
4. Đủ dễ hiểu để user tin

## 9. Video YouTube nên xem

- [Introduction to Recommendation System](https://www.youtube.com/watch?v=gxXn9LDAdcU)
- [Having Fun with Recommender Systems](https://www.youtube.com/watch?v=B7kwiUZhMew)

## 10. Web dễ đọc thêm

- [Google Developers - Recommendation Systems](https://developers.google.com/machine-learning/recommendation)
- [Machine Learning Cơ Bản](https://machinelearningcoban.com/)

