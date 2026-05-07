# 04. Hybrid, Cold-Start và Đa Dạng

## 1. Vì sao hybrid gần như là bắt buộc?

Nếu chỉ dùng content-based:

- dễ lặp gu cũ
- khó khám phá

Nếu chỉ dùng collaborative filtering:

- người mới gần như không có dữ liệu
- item mới cũng khó được gợi ý

Vì vậy production thường đi theo:

> Hybrid = trộn nhiều tín hiệu + thêm luật nghiệp vụ

## 2. Công thức hybrid đơn giản

\[
Score(u,i)=\alpha \cdot CB(u,i)+\beta \cdot CF(u,i)+\gamma \cdot Business(i,u)
\]

Trong đó:

- `CB`: content-based score
- `CF`: collaborative score
- `Business`: stock, locality, seasonality, seller quality, delivery, diversity bonus

Ví dụ:

\[
Score = 0.35 \cdot CB + 0.4 \cdot CF + 0.25 \cdot Business
\]

## 3. Ví dụ số

Giả sử item A:

- `CB = 0.9`
- `CF = 0.4`
- `Business = 0.8`

\[
Score(A)=0.35\cdot0.9 + 0.4\cdot0.4 + 0.25\cdot0.8 = 0.675
\]

Item B:

- `CB = 0.5`
- `CF = 0.9`
- `Business = 0.7`

\[
Score(B)=0.35\cdot0.5 + 0.4\cdot0.9 + 0.25\cdot0.7 = 0.71
\]

Kết quả:

- item B đứng trên A

## 4. Cold-start là gì?

Cold-start là bài toán:

- user mới chưa có dữ liệu
- item mới chưa có dữ liệu
- seller mới chưa có dữ liệu

Trong Fresh Farm, case nổi bật là:

- user mới chưa mua gì

## 5. Chiến lược cho user mới

### Giai đoạn 1: chưa có lịch sử

Ưu tiên:

- item nổi bật
- item đúng mùa
- item gần vùng
- item đa dạng seller/category/origin

### Giai đoạn 2: đã có 1-3 tương tác

Bắt đầu cá nhân hóa theo:

- category vừa xem
- origin vừa xem
- seller vừa ghé
- query vừa tìm

### Giai đoạn 3: đã có đơn hàng

Mở mạnh:

- buy again
- favorite shop
- basket affinity
- replenishment

## 6. Diversity là gì?

Diversity nghĩa là:

> Trong top-k, không để tất cả item quá giống nhau.

Đa dạng có thể theo:

- seller
- category
- origin
- price band

## 7. Ví dụ toán học về diversity

Giả sử top-4 theo score thuần là:

1. Seller 4 - Rau muống
2. Seller 4 - Cải ngọt
3. Seller 4 - Rau dền
4. Seller 4 - Mồng tơi

Nếu áp luật:

- mỗi seller tối đa 1 item ở first pass khi `limit <= 4`

thì top-4 có thể thành:

1. Seller 4 - Rau muống
2. Seller 2 - Cải xanh
3. Seller 47 - Bí đỏ
4. Seller 51 - Nấm rơm

## 8. Discovery slot là gì?

Discovery slot là một chỗ cố tình dành ra để đưa item “đáng khám phá” lên top-k.

Ví dụ:

- top-8 đang bị category preference chiếm hết
- hệ thống vẫn cố nhét 1 item từ:
  - shop bạn hay mua
  - shop bạn vừa quay lại

## 9. Hybrid trong Fresh Farm

Fresh Farm hiện đi theo hướng trộn:

- content-based
- collaborative filtering
- user-product score
- user-category score
- user-seller score
- basket affinity
- replenishment profile
- seller/category/origin diversity
- discovery slot

## 10. Điều cần nhớ nhất

Hybrid không chỉ là cộng 2 con số.

Nó còn gồm:

- điều kiện bật/tắt từng nguồn tín hiệu
- fallback khi tín hiệu thiếu
- cold-start policy
- diversity rules
- explanation rules

## 11. Video YouTube nên xem

- [Introduction to Recommendation System](https://www.youtube.com/watch?v=gxXn9LDAdcU)
- [Hands on - Build a Recommender system](https://www.youtube.com/watch?v=juU7m9rOAqo)

## 12. Web dễ đọc thêm

- [Google Developers - Recommendation Systems](https://developers.google.com/machine-learning/recommendation)
- [Recommendation Systems at Scale](https://engineersofai.com/docs/ai-systems/case-studies/Recommendation-Systems)
