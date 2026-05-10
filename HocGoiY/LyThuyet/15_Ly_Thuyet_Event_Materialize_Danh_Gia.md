# 06. Dữ Liệu Sự Kiện, Materialize và Đánh Giá

## 1. Recommendation không sống bằng công thức thôi

Muốn recommendation chạy tốt lâu dài, bạn cần cả 3 lớp:

1. Event
2. Materialized signal
3. Evaluation

## 2. Event là gì?

Event là hành vi thô của user.

Ví dụ:

- xem sản phẩm
- tìm kiếm từ khóa
- click vào item được gợi ý
- mua hàng

## 3. Vì sao không dùng event thô trực tiếp mãi mãi?

Vì event thô thường:

- quá nhiều
- khó truy vấn nhanh
- không tiện cho request realtime

Nên mới có bước materialize.

## 4. Materialized signal là gì?

Materialized signal là dữ liệu đã được tổng hợp sẵn.

Ví dụ:

- `user-product score`
- `user-category score`
- `user-seller score`
- `basket affinity`
- `replenishment profile`

## 5. Ví dụ quy đổi event thành score

Giả sử đặt:

- 1 lần view = 1 điểm
- 1 lần search click = 3 điểm
- 1 lần recommendation click = 4 điểm
- 1 lần purchase = 10 điểm

Nếu user với item X có:

- 5 view
- 2 search click
- 1 purchase

Ta có:

\[
Score = 5 \cdot 1 + 2 \cdot 3 + 1 \cdot 10 = 21
\]

## 6. Recency vì sao quan trọng?

Hành vi hôm qua thường giá trị hơn hành vi 6 tháng trước.

Một công thức decay quen thuộc:

\[
WeightedScore = RawScore \cdot e^{-\lambda t}
\]

Trong đó:

- \(t\): số ngày đã trôi qua
- \(\lambda\): hệ số decay

Ví dụ:

- `RawScore = 20`
- \(\lambda = 0.1\)
- \(t = 1\)

\[
20 \cdot e^{-0.1} \approx 18.1
\]

Nếu \(t = 10\):

\[
20 \cdot e^{-1} \approx 7.36
\]

## 7. Basket affinity là gì?

Basket affinity trả lời:

> Người mua món A có hay mua thêm món B không?

Ví dụ:

- 100 đơn có rau muống
- 40 đơn trong số đó có thêm nấm rơm

\[
P(B|A)=\frac{40}{100}=0.4
\]

## 8. Replenishment profile là gì?

Replenishment profile trả lời:

> User này có khả năng sắp cần mua lại món đó chưa?

Ví dụ:

- user mua trứng trung bình 7 ngày một lần
- lần mua gần nhất là 6 ngày trước

Suy ra:

- món này sắp đến kỳ mua lại

## 9. Vì sao cần refresh nền?

Nếu mỗi event đều tính lại toàn bộ score ngay trong request:

- chậm
- dễ timeout
- khó scale

Nên production thường dùng:

1. request ghi event
2. phát tín hiệu refresh
3. background service tính lại score

## 10. Evaluation là gì?

Evaluation là cách kiểm tra recommendation có tốt hơn không.

Nếu chỉ nhìn bằng mắt thì rất dễ ảo tưởng.

## 11. Một số chỉ số dễ hiểu

### Precision trực giác

Top-k có bao nhiêu item thật sự liên quan.

Ví dụ:

- top-5 có 3 item phù hợp

\[
Precision@5 = \frac{3}{5} = 0.6
\]

### Coverage

Hệ thống có chỉ quanh quẩn vài item không.

### Diversity

Top-k có quá lặp seller/category/origin không.

### Unique seller count

Top-4 có 4 seller khác nhau thường nhìn tốt hơn top-4 toàn 1 seller.

### Fallback rate

Hệ thống có đang dựa quá nhiều vào fallback không.

## 12. Fresh Farm đang làm đúng hướng ở đâu?

Trong code hiện tại:

- ghi nhận event
- build affinity
- materialize score
- background refresh
- BFF đọc score để xếp hạng
- có script evaluation để đo output runtime

## 13. Điều cần nhớ nhất

Recommendation mạnh không chỉ vì:

- thuật toán hay

Mà còn vì:

- event ghi đủ đúng
- score được làm mới đúng lúc
- có đo chất lượng sau mỗi thay đổi

## 14. Video YouTube nên xem

- [Collaborative filtering for recommendation systems in Python](https://www.youtube.com/watch?v=z0dx-YckFko)
- [Introduction to Recommendation System](https://www.youtube.com/watch?v=gxXn9LDAdcU)

## 15. Web dễ đọc thêm

- [Google Developers - Recommendation Systems](https://developers.google.com/machine-learning/recommendation)
- [Recommenders Team GitHub](https://github.com/recommenders-team/recommenders)

