# 05. Ranking, Reranking và Chia Section

## 1. Vì sao chưa thể lấy top-k theo điểm là xong?

Nếu chỉ:

1. tính điểm
2. sort giảm dần
3. lấy top-k

thì dễ sinh ra:

- lặp seller
- lặp category
- lặp origin
- thiếu item khám phá
- lý do gợi ý đơn điệu

Vì vậy production hay có 2 tầng:

1. `ranking`
2. `reranking`

## 2. Ranking là gì?

Ranking là giai đoạn tính điểm gốc.

Ví dụ:

\[
BaseScore(u,i)=
0.4\cdot CF +
0.3\cdot CB +
0.15\cdot SellerAffinity +
0.15\cdot Utility
\]

## 3. Reranking là gì?

Reranking là sửa lại danh sách sau ranking để phục vụ trải nghiệm tốt hơn.

Ví dụ:

- ép mỗi seller tối đa 2 item ở first pass
- nếu top-k thiếu item từ shop vừa quay lại thì chèn vào
- nếu top-k thiếu item theo long-term preference thì chèn vào

Nói ngắn:

> Ranking tối ưu điểm số, reranking tối ưu trải nghiệm.

## 4. Ví dụ số

| Item | Seller | Score |
|---|---|---:|
| A | 4 | 9.4 |
| B | 4 | 9.1 |
| C | 4 | 8.8 |
| D | 2 | 8.5 |
| E | 47 | 8.3 |

Nếu lấy top-4 thuần:

- A, B, C, D

Nếu áp first-pass seller cap = 1:

- A, D, E, B

Danh sách sau tuy không giữ điểm thuần tối đa, nhưng UX tốt hơn.

## 5. Section là gì?

Section là cách chia recommendation thành các nhóm có ý nghĩa.

Ví dụ trong Fresh Farm:

- `for_you`
- `today_highlights`
- `buy_again`
- `recent_shop`
- `favorite_shop`
- `buy_with_history`
- `replenish_soon`
- `seasonal_local`

## 6. Vì sao phải chia section?

Nếu đổ hết vào một danh sách duy nhất, user sẽ khó hiểu:

- tại sao sản phẩm này lại xuất hiện
- đây là mua lại hay khám phá
- đây là do shop quen hay do đúng mùa

## 7. Tư duy multi-objective ranking

\[
FinalScore = Relevance + Utility + DiversityBonus + DiscoveryBonus - RepetitionPenalty
\]

Trong đó:

- `Relevance`: hợp user
- `Utility`: hàng tốt, còn hàng, hợp vận hành
- `DiversityBonus`: thưởng nếu top-k đa dạng hơn
- `DiscoveryBonus`: thưởng nếu giúp user khám phá
- `RepetitionPenalty`: phạt nếu lặp seller/cụm quá nhiều

## 8. Ví dụ logical về discovery slot

Giả sử top-8 hiện tại toàn item có điểm category rất cao.

Nhưng user vừa quay lại shop X hôm qua.

Nếu không có discovery slot:

- shop X có thể không xuất hiện

Nếu có discovery slot:

- hệ thống thay item cuối top-8 bằng một item từ shop X

## 9. Reason và tag quan trọng thế nào?

Recommendation tốt mà không giải thích được thì user vẫn thấy “ảo”.

Ví dụ:

- “Từ shop bạn hay mua”
- “Đúng vùng Long An bạn hay chọn”
- “Có thể đang đến kỳ mua lại”

## 10. Ranking trong Fresh Farm

Khi đọc code bạn sẽ thấy:

- tính điểm theo nhiều loại signal
- sau đó lọc/rerank
- sau đó chèn discovery candidate
- sau đó map thành reason/tag
- sau đó mới chia section

## 11. Điều cần nhớ nhất

Recommendation production tốt không phải là:

- “mô hình thông minh nhất”

Mà là:

- “pipeline quyết định hiển thị thông minh và có kiểm soát”.

## 12. Video YouTube nên xem

- [Having Fun with Recommender Systems](https://www.youtube.com/watch?v=B7kwiUZhMew)
- [Hands on - Build a Recommender system](https://www.youtube.com/watch?v=juU7m9rOAqo)

## 13. Web dễ đọc thêm

- [Recommendation Systems at Scale](https://engineersofai.com/docs/ai-systems/case-studies/Recommendation-Systems)
- [Recommendation System Design](https://www.systemdesignhandbook.com/guides/recommendation-system-design/)
