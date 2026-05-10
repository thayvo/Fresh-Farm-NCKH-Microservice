# 00. Bắt đầu ở đây - hướng dẫn học module gợi ý

Đây là file nên mở đầu tiên khi học phần recommendation/gợi ý của FreshFarm.

Mục tiêu của bộ tài liệu:

- Biết nên học tài liệu nào trước, tài liệu nào sau.
- Hiểu module gợi ý từ lý thuyết đến code.
- Biết công thức tính điểm nằm ở đâu trong source.
- Có thể thuyết minh lại phần gợi ý trong báo cáo hoặc khi demo.

## 1. Nếu chỉ muốn học nhanh

Đọc theo thứ tự này:

1. `01_Giao_Trinh_Tong_Hop_Hoc_Tu_Ly_Thuyet_Den_Code.md`
   - File quan trọng nhất.
   - Học từ tổng quan, lý thuyết, công thức toán đến mapping source code.
2. `02_Nhung_Phan_Da_Lam_Trong_Module_Goi_Y.md`
   - Biết dự án đã làm những phần nào trong module gợi ý.
   - Dùng tốt khi viết báo cáo hoặc chuẩn bị thuyết trình.
3. `03_Lo_Trinh_Doc_Source_Code_Recommendation.md`
   - Dùng khi bắt đầu mở source code để đọc.
   - Chỉ rõ nên đọc file BFF, Ordering, service, model theo thứ tự nào.

## 2. Nếu muốn học chắc lý thuyết

Sau khi đọc file `01`, đọc tiếp nhóm `LyThuyet` theo thứ tự:

1. `LyThuyet/10_Ly_Thuyet_Tong_Quan_He_Thong_Goi_Y.md`
2. `LyThuyet/11_Ly_Thuyet_Content_Based_Filtering.md`
3. `LyThuyet/12_Ly_Thuyet_Collaborative_Filtering.md`
4. `LyThuyet/13_Ly_Thuyet_Hybrid_Cold_Start_Da_Dang.md`
5. `LyThuyet/14_Ly_Thuyet_Ranking_Reranking_Chia_Section.md`
6. `LyThuyet/15_Ly_Thuyet_Event_Materialize_Danh_Gia.md`

## 3. Công nghệ chính trong phần gợi ý

- `C#`: API, scoring, ranking, reranking, chia section recommendation và materialize signal.
- `ASP.NET Core/Web BFF`: lấy dữ liệu từ nhiều service và quyết định dữ liệu trả về giao diện.
- `Ordering API`: lưu event, tính affinity, tính điểm user/category/seller/product và phục vụ Product Insights API.
- `PowerShell`: chạy script đánh giá nhanh output recommendation khi cần kiểm tra runtime.

## 4. Ghi chú

- Các file `.docx` là bản Word để đọc/học dễ hơn.
- Các file `.md` là bản Markdown tiện theo dõi trong repo.
- Các file trong `HocGoiY/src` là bản sao học tập của source liên quan đến recommendation, không phải nơi chính để sửa production code.
