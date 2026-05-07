# 02. Content-Based Filtering

## 1. Ý tưởng cốt lõi

Content-based filtering nghĩa là:

> User từng thích item nào thì hệ thống tìm các item có thuộc tính giống item đó.

Ví dụ trong Fresh Farm:

- user hay xem `rau muống`, `cải xanh`
- hệ thống có thể gợi ý:
  - rau dền
  - mồng tơi
  - cải bẹ

## 2. Dữ liệu cần có

Phương pháp này cần mô tả item bằng feature.

Ví dụ một sản phẩm có thể có:

- category
- origin
- mùa vụ
- khoảng giá
- seller
- trạng thái giao nhanh

Ta biểu diễn item thành vector.

Ví dụ:

\[
x_{rau\_muong} = [1, 0, 0, 1, 0, 1]
\]

Có thể hiểu là:

- rau lá = 1
- trái cây = 0
- nhập khẩu = 0
- vùng miền Nam = 1
- giá cao = 0
- đúng mùa = 1

## 3. User profile

Ta cũng biểu diễn user thành vector sở thích.

Ví dụ user hay xem:

- rau muống
- cải ngọt
- rau dền

Khi đó vector sở thích có thể gần với:

\[
p_u = [0.9, 0.1, 0.0, 0.8, 0.2, 0.9]
\]

## 4. Cách tính điểm cơ bản

Một cách rất phổ biến là cosine similarity:

\[
\text{sim}(u, i) = \frac{p_u \cdot x_i}{||p_u|| \cdot ||x_i||}
\]

Nếu hai vector cùng hướng thì similarity cao.

## 5. Ví dụ toán học

Giả sử:

\[
p_u = [0.9, 0.8, 0.1]
\]

Hai item:

- Item A: `Rau muống đúng mùa`
  \[
  x_A = [1, 1, 0]
  \]
- Item B: `Táo nhập giá cao`
  \[
  x_B = [0, 0, 1]
  \]

Tích vô hướng:

- Với A:
  \[
  p_u \cdot x_A = 1.7
  \]
- Với B:
  \[
  p_u \cdot x_B = 0.1
  \]

Rõ ràng A phù hợp hơn B rất nhiều.

## 6. Ưu điểm

- Dễ hiểu
- Không cần dữ liệu từ user khác
- Hợp với item có mô tả rõ
- Giải thích dễ

Ví dụ explanation:

- “Hợp nhóm rau lá bạn hay xem”
- “Đúng vùng Long An bạn hay chọn”
- “Đúng mùa gần bạn”

## 7. Nhược điểm

- Dễ lặp gu cũ
- Khó khám phá
- Phụ thuộc chất lượng feature
- Không tận dụng sức mạnh của cộng đồng user

## 8. Content-based trong Fresh Farm

Fresh Farm không dùng content-based thuần.

Nó đi cùng:

- category
- origin/locality
- seasonality
- quality/stock
- seller diversity

Tức là:

> content-based + business utility

## 9. Ví dụ logic trong Fresh Farm

Giả sử user mới chưa mua gì nhưng vừa xem:

- `Rau muống Long An`

Khi đó hệ thống có thể ưu tiên:

1. Rau dền Long An
2. Cải ngọt Long An
3. Mồng tơi miền Tây

Thay vì:

1. Táo nhập khẩu
2. Kiwi
3. Lựu Peru

## 10. Công thức đơn giản nên nhớ

\[
Score(u, i) = \sum_k w_k \cdot feature_k(i)
\]

Trong đó:

- \(w_k\) là mức user thích feature thứ \(k\)
- \(feature_k(i)\) là item có feature đó mạnh đến đâu

## 11. Video YouTube nên xem

- [Hands on - Build a Recommender system](https://www.youtube.com/watch?v=juU7m9rOAqo)
- [Book Recommender System - Machine Learning Project](https://www.youtube.com/watch?v=1YoD0fg3_EM)

## 12. Web dễ đọc thêm

- [Google Developers - Recommendation Systems](https://developers.google.com/machine-learning/recommendation)
- [GeeksforGeeks - Recommendation System in Python](https://www.geeksforgeeks.org/machine-learning/recommendation-system-in-python/)
