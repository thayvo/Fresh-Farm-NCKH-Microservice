# 03. Collaborative Filtering

## 1. Ý tưởng cốt lõi

Collaborative filtering nghĩa là:

> Không cần nhìn item giống nhau ra sao. Chỉ cần biết những người giống bạn đã thích gì.

Có 2 cách nhìn phổ biến:

1. User-user
2. Item-item

## 2. Ví dụ đời thường

Giả sử:

- User A mua:
  - rau muống
  - cải xanh
- User B mua:
  - rau muống
  - cải xanh
  - nấm rơm

Nếu A và B khá giống nhau, hệ thống có thể gợi ý `nấm rơm` cho A.

## 3. Ma trận user-item

Collaborative filtering thường bắt đầu từ ma trận:

\[
R =
\begin{bmatrix}
r_{11} & r_{12} & \dots \\
r_{21} & r_{22} & \dots \\
\vdots & \vdots & \ddots
\end{bmatrix}
\]

Ví dụ:

| User | Rau muống | Cải xanh | Nấm rơm |
|---|---:|---:|---:|
| A | 1 | 1 | 0 |
| B | 1 | 1 | 1 |
| C | 0 | 0 | 1 |

Ở đây:

- A gần B hơn C
- nên Nấm rơm có thể được gợi ý cho A

## 4. User-user similarity

Một cách đơn giản:

\[
sim(A, B) = \frac{A \cdot B}{||A|| \cdot ||B||}
\]

Với:

- \(A = [1,1,0]\)
- \(B = [1,1,1]\)

Ta có:

\[
A \cdot B = 2
\]

\[
||A|| = \sqrt{2}, \quad ||B|| = \sqrt{3}
\]

\[
sim(A,B)=\frac{2}{\sqrt{6}} \approx 0.816
\]

Khá cao.

## 5. Item-item similarity

Thay vì so user với user, ta cũng có thể so item với item.

Ví dụ:

- người mua `rau muống` thường cũng mua `cải xanh`
- người mua `cải xanh` thường cũng mua `nấm rơm`

Từ đó suy ra item gần nhau theo hành vi.

## 6. Matrix factorization trực giác là gì?

Ý tưởng:

\[
R \approx P Q^T
\]

Trong đó:

- \(P\): vector sở thích ẩn của user
- \(Q\): vector tính chất ẩn của item

Điểm dự đoán:

\[
\hat r_{ui} = p_u \cdot q_i
\]

Ví dụ:

- user A:
  \[
  p_A = [0.9, 0.2]
  \]
- item Nấm rơm:
  \[
  q_{nam} = [0.8, 0.3]
  \]

Khi đó:

\[
\hat r_{A,nam} = 0.9 \cdot 0.8 + 0.2 \cdot 0.3 = 0.78
\]

## 7. Ưu điểm

- Bắt được mẫu hành vi rất hay
- Gợi ý được món không quá giống về thuộc tính nhưng hợp về hành vi
- Tốt cho “mua kèm”, “người giống bạn cũng thích”, “sản phẩm tương tự”

## 8. Nhược điểm

- Cold-start khó
- Cần đủ dữ liệu tương tác
- Giải thích khó hơn content-based

## 9. Collaborative filtering trong Fresh Farm

Trong Fresh Farm, collaborative signal hữu ích cho:

- `home-collaborative`
- `similar`
- `search-ranking`
- `basket affinity`

Ví dụ:

- user mua rau muống và cải ngọt
- nhiều user giống họ cũng mua nấm rơm
- hệ thống gợi ý nấm rơm

## 10. Công thức trực giác nên nhớ

\[
Score(u, i) \propto \sum_{v \in N(u)} sim(u,v)\cdot r_{v,i}
\]

Ý nghĩa:

- lấy những user gần với user \(u\)
- xem họ thích item \(i\) đến đâu
- cộng lại theo trọng số similarity

## 11. Video YouTube nên xem

- [Collaborative filtering for recommendation systems in Python](https://www.youtube.com/watch?v=z0dx-YckFko)
- [Matrix Factorization](https://www.youtube.com/watch?v=d7iIb_XVkZs)

## 12. Web dễ đọc thêm

- [Google Developers - Recommendation Systems](https://developers.google.com/machine-learning/recommendation)
- [Krython - Collaborative Filtering tutorial](https://krython.com/tutorial/python/recommendation-systems-collaborative-filtering/)
