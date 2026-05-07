from __future__ import annotations

import argparse
from pathlib import Path

from openpyxl import load_workbook


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_WORKBOOK_PATH = REPO_ROOT / "output" / "spreadsheet" / "recommendation_product_data_review.xlsx"
BATCH2_COLLECTION_TIMESTAMP = "2026-04-24 21:12:38 ICT"


SEASONALITY_ROWS = [
    (
        "B2",
        46,
        "Khoai lang Nhật",
        "Củ & rễ",
        "Nhật Bản (trồng VN)",
        "Province",
        "Bắc Giang, Việt Nam",
        "2 vụ/năm: vụ Xuân và vụ Đông",
        2,
        12,
        "Tháng 5 âm lịch; tháng 12 âm lịch",
        "Yes",
        "Khuyến nông Quốc gia ghi khoai lang Nhật tại Bắc Giang trồng được 2 vụ/năm, khoảng 4-5 tháng/vụ; vụ Xuân trồng sau Tết, thu đầu tháng 5 âm lịch; vụ Đông trồng cuối tháng 8-đầu tháng 9, thu tháng 12 âm lịch.",
        "Bắc Giang: Trồng khoai thu nhập trên 120 -150 triệu đồng/ha/vụ",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/bac-giang-trong-khoai-thu-nhap-tren-120-150-trieu-donghavu-15779.html",
        "2017-06-28",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn xác nhận khoai lang Nhật trồng tại Bắc Giang, không xác nhận riêng SKU hiện tại hoặc chứng nhận GlobalGAP.",
        "",
    ),
    (
        "B2",
        46,
        "Khoai lang Nhật",
        "Củ & rễ",
        "Nhật Bản (trồng VN)",
        "Province",
        "Tây Ninh, Việt Nam",
        "Đông Xuân; mô hình phù hợp khí hậu Tây Ninh",
        12,
        4,
        "Đông Xuân",
        "No",
        "Khuyến nông Quốc gia ghi mô hình khoai lang tím Nhật tại Tây Ninh thu hoạch vụ Đông Xuân, phù hợp thổ nhưỡng và khí hậu địa phương.",
        "Khoai lang tím Nhật - Cây trồng tiềm năng cho nông dân Tây Ninh",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/khoai-lang-tim-nhat-cay-trong-tiem-nang-cho-nong-dan-tay-ninh-31095.html",
        "2025-04-25",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn là khoai lang tím Nhật tại Tây Ninh, không phải mọi loại khoai lang Nhật.",
        "",
    ),
    (
        "B2",
        52,
        "Nấm hương khô",
        "Nấm",
        "Trung Quốc",
        "Country",
        "Trung Quốc",
        "Sản phẩm khô/chế biến; mùa vụ tươi phụ thuộc vùng và mô hình trồng",
        "",
        "",
        "Không áp dụng một mùa bán lẻ cố định",
        "No",
        "FAO ghi Trung Quốc là nhà sản xuất lớn của nấm hương và xuất khẩu nấm hương khô; nguồn không cung cấp lịch mùa vụ chi tiết theo vùng.",
        "Non-Wood Forest Products in 15 Countries of Tropical Asia: An Overview",
        "FAO",
        "https://fao.org/docrep/fao/005/AB598E/AB598E00.pdf",
        "2002",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn xác nhận sản xuất/xuất khẩu nấm hương khô Trung Quốc, không xác nhận mùa vụ cụ thể của SKU.",
        "",
    ),
    (
        "B2",
        53,
        "Nấm linh chi đỏ",
        "Nấm",
        "Hàn Quốc (trồng VN)",
        "Province",
        "Hòa Bình, Việt Nam",
        "Nuôi trồng theo lứa; lứa đầu khoảng 3 tháng, lứa sau 70-80 ngày",
        "",
        "",
        "Theo chu kỳ nuôi trồng, không phải mùa tự nhiên cố định",
        "No",
        "Khuyến nông Quốc gia ghi mô hình nấm linh chi đỏ trên giá thể gỗ keo tươi tại Hòa Bình: khoảng 3 tháng cho thu hoạch lứa đầu, một bịch có thể thu 3 lần, lứa sau khoảng 70-80 ngày.",
        "Hòa Bình: Trồng nấm linh chi trên giá thể gỗ keo tươi - mô hình kinh tế hiệu quả",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/hoa-binh-trong-nam-linh-chi-tren-gia-the-go-keo-tuoi--mo-hinh-kinh-te-hieu-qua-21892.html",
        "2022-06-13",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn xác nhận nấm linh chi đỏ trồng tại Việt Nam, chưa xác nhận giống/nguồn gốc Hàn Quốc của SKU.",
        "",
    ),
    (
        "B2",
        115,
        "Tỏi tây",
        "Rau thơm & gia vị",
        "Trung Quốc",
        "Country",
        "Trung Quốc",
        "Allium trồng rộng ở Trung Quốc; chưa có mùa tỏi tây nhập khẩu cụ thể",
        "",
        "",
        "Cần kiểm chứng thêm theo nhà cung cấp nhập khẩu",
        "No",
        "ISHS ghi nhóm rau Allium là cây rau quan trọng tại Trung Quốc, nhưng dữ liệu công khai tìm được không đủ để xác nhận lịch mùa vụ riêng cho tỏi tây nhập khẩu.",
        "ALLIUM PRODUCTION AND RESEARCH IN CHINA",
        "International Society for Horticultural Science",
        "https://www.ishs.org/ishs-article/358_19",
        "1994",
        "Medium",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn nói nhóm Allium tại Trung Quốc, không xác nhận riêng tỏi tây/Allium porrum và không có lịch mùa vụ nhập khẩu.",
        "",
    ),
    (
        "B2",
        121,
        "khoai mỡ",
        "Rau lá",
        "",
        "Province",
        "Vĩnh Long, Việt Nam",
        "Xuống giống sau lúa Đông Xuân; thu sau khoảng 6 tháng",
        "",
        "",
        "Sau Đông Xuân + 6 tháng",
        "No",
        "Khuyến nông Quốc gia ghi khoai mỡ ở Mang Thít, Vĩnh Long xuống giống khi thu hoạch lúa Đông Xuân và sau 6 tháng trồng thì thu hoạch; có mô hình VietGAP từ Trung tâm Khuyến nông Vĩnh Long.",
        "Vĩnh Long: Tuân thủ kỹ thuật canh tác - Cánh đồng khoai mỡ ngày càng mở rộng",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/vinh-long-tuan-thu-ky-thuat-canh-tac--canh-dong-khoai-mo-ngay-cang-mo-rong-19139.html",
        "2019-08-22",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Dòng catalog thiếu ProductInfo; nguồn không xác nhận origin/standard hiện hành của sản phẩm trong hệ thống.",
        "",
    ),
    (
        "B2",
        122,
        "Rau muống",
        "Rau lá",
        "",
        "Country",
        "Việt Nam",
        "Có thể sản xuất trong hệ thống thủy canh tuần hoàn; thu hái nhiều lứa",
        "",
        "",
        "Sau mỗi đợt thu hái bổ sung dinh dưỡng cho lứa sau",
        "No",
        "Khuyến nông Quốc gia ghi rau muống là một trong các giống có thể sản xuất thủy canh tuần hoàn; sau khi hái lứa đầu mới bổ sung dinh dưỡng cho lứa sau.",
        "Quy trình kỹ thuật sản xuất rau thủy canh tuần hoàn",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/ky-thuat-trong-trot/quy-trinh-ky-thuat-san-xuat-rau-thuy-canh-tuan-hoan-759.html",
        "2011-12-26",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Dòng catalog thiếu ProductInfo; nguồn là quy trình kỹ thuật thủy canh chung, chưa xác nhận origin/standard hiện hành.",
        "",
    ),
]


RESEARCH_ROWS = [
    (
        "B2",
        46,
        "Khoai lang Nhật",
        "Origin/seasonality context",
        "Giống khoai lang Nhật đã được trồng tại Việt Nam; Bắc Giang có 2 vụ/năm, Tây Ninh có mô hình khoai lang tím Nhật vụ Đông Xuân.",
        "Bắc Giang; Tây Ninh",
        "Bắc Giang: Trồng khoai thu nhập trên 120 -150 triệu đồng/ha/vụ",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/bac-giang-trong-khoai-thu-nhap-tren-120-150-trieu-donghavu-15779.html",
        "2017-06-28",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Chưa xác nhận GlobalGAP và chưa xác nhận SKU đang bán lấy từ vùng nào.",
        "",
    ),
    (
        "B2",
        52,
        "Nấm hương khô",
        "Origin/processing context",
        "Trung Quốc là nguồn sản xuất lớn của nấm hương; FAO ghi có xuất khẩu nấm hương khô. Sản phẩm khô nên tính mùa vụ bán lẻ cần kiểm chứng theo lô/nhà cung cấp.",
        "Trung Quốc",
        "Non-Wood Forest Products in 15 Countries of Tropical Asia: An Overview",
        "FAO",
        "https://fao.org/docrep/fao/005/AB598E/AB598E00.pdf",
        "2002",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Chưa xác nhận chứng nhận Organic và mùa vụ của SKU.",
        "",
    ),
    (
        "B2",
        52,
        "Nấm hương khô",
        "Industry/current context",
        "People's Daily Online dẫn dữ liệu chính thức cho biết Trung Quốc sản xuất hơn 40 triệu tấn nấm ăn mỗi năm và Hubei/Suizhou có sản phẩm nấm hương chế biến/xuất khẩu.",
        "Trung Quốc",
        "China becomes world's largest producer, consumer of edible mushrooms",
        "People's Daily Online",
        "https://en.people.cn/n3/2025/0930/c98649-20373212.html",
        "2025-09-30",
        "Medium",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn sau ngày thu thập hiện tại theo hệ thống tìm kiếm; cần kiểm chứng lại khi dùng chính thức.",
        "",
    ),
    (
        "B2",
        53,
        "Nấm linh chi đỏ",
        "Cultivation/preservation context",
        "Mô hình Hòa Bình cho thấy nấm linh chi đỏ thu hoạch theo lứa; quả thể sau thu hái được vệ sinh, cắt chân và sấy ở 40-45 độ C.",
        "Hòa Bình",
        "Hòa Bình: Trồng nấm linh chi trên giá thể gỗ keo tươi - mô hình kinh tế hiệu quả",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/hoa-binh-trong-nam-linh-chi-tren-gia-the-go-keo-tuoi--mo-hinh-kinh-te-hieu-qua-21892.html",
        "2022-06-13",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Chưa xác nhận giống Hàn Quốc hoặc tiêu chuẩn dược liệu cụ thể của SKU.",
        "",
    ),
    (
        "B2",
        115,
        "Tỏi tây",
        "Origin/seasonality context",
        "Tỏi tây thuộc nhóm Allium; nguồn ISHS xác nhận Allium là nhóm rau quan trọng và trồng rộng tại Trung Quốc, nhưng chưa đủ dữ liệu để chốt mùa tỏi tây Trung Quốc.",
        "Trung Quốc",
        "ALLIUM PRODUCTION AND RESEARCH IN CHINA",
        "International Society for Horticultural Science",
        "https://www.ishs.org/ishs-article/358_19",
        "1994",
        "Medium",
        BATCH2_COLLECTION_TIMESTAMP,
        "Nguồn chưa nói riêng tỏi tây; cần xác nhận với chứng từ nhập khẩu hoặc nhà cung cấp.",
        "",
    ),
    (
        "B2",
        121,
        "khoai mỡ",
        "Origin/seasonality context",
        "Vĩnh Long có vùng trồng khoai mỡ tại Mang Thít; có mô hình VietGAP, xuống giống sau lúa Đông Xuân và thu sau khoảng 6 tháng.",
        "Vĩnh Long",
        "Vĩnh Long: Tuân thủ kỹ thuật canh tác - Cánh đồng khoai mỡ ngày càng mở rộng",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/vinh-long-tuan-thu-ky-thuat-canh-tac--canh-dong-khoai-mo-ngay-cang-mo-rong-19139.html",
        "2019-08-22",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Catalog thiếu ProductInfo nên chưa đối chiếu được nguồn gốc sản phẩm nội bộ.",
        "",
    ),
    (
        "B2",
        122,
        "Rau muống",
        "Cultivation/availability context",
        "Rau muống có thể sản xuất thủy canh tuần hoàn, thu hái nhiều lứa; nguồn không nêu origin hoặc tiêu chuẩn của sản phẩm trong catalog.",
        "Việt Nam",
        "Quy trình kỹ thuật sản xuất rau thủy canh tuần hoàn",
        "Trung tâm Khuyến nông Quốc gia",
        "https://khuyennongvn.gov.vn/ky-thuat-trong-trot/quy-trinh-ky-thuat-san-xuat-rau-thuy-canh-tuan-hoan-759.html",
        "2011-12-26",
        "High",
        BATCH2_COLLECTION_TIMESTAMP,
        "Catalog thiếu ProductInfo nên chưa đối chiếu được nguồn gốc/standard nội bộ.",
        "",
    ),
]


PRODUCT_SUMMARIES = {
    46: {
        "Review Status": "Partially verified - batch 2",
        "Verified Seasonality Summary": "Bắc Giang: khoai lang Nhật trồng 2 vụ/năm, vụ Xuân thu đầu tháng 5 âm lịch và vụ Đông thu tháng 12 âm lịch; Tây Ninh có mô hình khoai lang tím Nhật vụ Đông Xuân.",
        "Seasonality Locality Count": 2,
        "Seasonality Evidence Count": 2,
        "General Evidence Count": 1,
        "Verified Origin Country": "Vietnam; Japan variety/origin label needs supplier confirmation",
        "Verified Origin Province/Region": "Bắc Giang; Tây Ninh",
        "Verified Domestic/Imported": "Domestic-grown / foreign variety label",
        "Verified Standard/Certification": "GlobalGAP chưa kiểm chứng từ nguồn công khai",
        "Verified Preservation Guidance": "Nơi khô thoáng; cần xác nhận theo quy cách đóng gói nội bộ",
        "Verified Flavor Profile": "Ngọt",
        "Verified Texture Profile": "Bở/dẻo tùy giống; nguồn không xác nhận SKU",
        "Verified Usage Profile": "Nướng, luộc, hấp",
        "Primary Source URL": "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/bac-giang-trong-khoai-thu-nhap-tren-120-150-trieu-donghavu-15779.html",
        "Secondary Source URL": "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/khoai-lang-tim-nhat-cay-trong-tiem-nang-cho-nong-dan-tay-ninh-31095.html",
        "Thời gian thu thập": BATCH2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Chưa có nguồn công khai xác nhận GlobalGAP, vùng cung ứng SKU hiện tại hoặc nhãn 'Nhật Bản (trồng VN)'.",
        "Reviewer Notes": "Nên đối chiếu chứng nhận/COA/nhà cung cấp trước khi nhập DB chính thức.",
    },
    52: {
        "Review Status": "Partially verified - batch 2",
        "Verified Seasonality Summary": "Nấm hương khô là sản phẩm chế biến/khô; mùa vụ bán lẻ không nên suy ra bằng lịch tươi nếu chưa có dữ liệu theo lô. Trung Quốc có sản xuất và xuất khẩu nấm hương khô.",
        "Seasonality Locality Count": 1,
        "Seasonality Evidence Count": 1,
        "General Evidence Count": 2,
        "Verified Origin Country": "China",
        "Verified Origin Province/Region": "China; Hubei/Suizhou context for processed shiitake",
        "Verified Domestic/Imported": "Imported",
        "Verified Standard/Certification": "Organic chưa kiểm chứng từ nguồn công khai",
        "Verified Preservation Guidance": "Nơi khô ráo, tránh ẩm; cần giữ kín bao bì sau mở",
        "Verified Flavor Profile": "Umami, mùi nấm đậm",
        "Verified Texture Profile": "Khô; mềm lại sau ngâm",
        "Verified Usage Profile": "Xào, nấu canh, hầm",
        "Primary Source URL": "https://fao.org/docrep/fao/005/AB598E/AB598E00.pdf",
        "Secondary Source URL": "https://en.people.cn/n3/2025/0930/c98649-20373212.html",
        "Thời gian thu thập": BATCH2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Chưa xác nhận chứng nhận Organic, tỉnh sản xuất, nhà xuất khẩu và mùa vụ cụ thể của SKU.",
        "Reviewer Notes": "Cần ưu tiên kiểm chứng bằng chứng từ nhập khẩu/nhãn sản phẩm.",
    },
    53: {
        "Review Status": "Partially verified - batch 2",
        "Verified Seasonality Summary": "Nấm linh chi đỏ trồng theo chu kỳ nuôi trồng: lứa đầu khoảng 3 tháng, có thể thu nhiều lứa cách nhau khoảng 70-80 ngày; không nên gán mùa tự nhiên cố định.",
        "Seasonality Locality Count": 1,
        "Seasonality Evidence Count": 1,
        "General Evidence Count": 1,
        "Verified Origin Country": "Vietnam cultivation verified; Korea origin label unverified",
        "Verified Origin Province/Region": "Hòa Bình; Lâm Đồng context cần kiểm chứng thêm nếu SKU ghi Đà Lạt",
        "Verified Domestic/Imported": "Domestic-grown / foreign-origin label needs supplier confirmation",
        "Verified Standard/Certification": "Dược liệu chưa kiểm chứng bằng tiêu chuẩn cụ thể",
        "Verified Preservation Guidance": "Dạng khô nên để nơi khô ráo, thoáng mát; nguồn ghi sấy 40-45 độ C sau thu hái",
        "Verified Flavor Profile": "Đắng nhẹ/đặc trưng dược liệu",
        "Verified Texture Profile": "Khô, cứng; dùng thái lát/hãm/nấu",
        "Verified Usage Profile": "Pha trà, sắc/nấu nước; không nên ghi nấu canh nếu chưa xác nhận mục đích bán",
        "Primary Source URL": "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/hoa-binh-trong-nam-linh-chi-tren-gia-the-go-keo-tuoi--mo-hinh-kinh-te-hieu-qua-21892.html",
        "Secondary Source URL": "",
        "Thời gian thu thập": BATCH2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Chưa xác nhận giống Hàn Quốc, tiêu chuẩn dược liệu và vùng cung ứng thật của SKU.",
        "Reviewer Notes": "Nên kiểm tra nhãn/COA vì đây là nhóm dược liệu.",
    },
    115: {
        "Review Status": "Partially verified - batch 2",
        "Verified Seasonality Summary": "Chưa đủ nguồn uy tín để chốt mùa vụ tỏi tây nhập từ Trung Quốc. Tạm ghi nhóm Allium trồng rộng tại Trung Quốc, cần kiểm chứng theo nhà cung cấp/lô nhập.",
        "Seasonality Locality Count": 1,
        "Seasonality Evidence Count": 1,
        "General Evidence Count": 1,
        "Verified Origin Country": "China",
        "Verified Origin Province/Region": "China; exact province unverified",
        "Verified Domestic/Imported": "Imported",
        "Verified Standard/Certification": "Import label chưa kiểm chứng bằng chứng từ",
        "Verified Preservation Guidance": "Nên bảo quản mát cho rau tươi; dữ liệu 'khô thoáng 2-3 tháng' cần kiểm tra lại vì có vẻ không phù hợp tỏi tây tươi.",
        "Verified Flavor Profile": "Hành nhẹ, ngọt nhẹ",
        "Verified Texture Profile": "Thân/lá tươi, giòn khi mới thu",
        "Verified Usage Profile": "Xào, nấu súp, hầm",
        "Primary Source URL": "https://www.ishs.org/ishs-article/358_19",
        "Secondary Source URL": "https://edibleplantdb.org/plants/536/allium-ampeloprasum-var-porrum",
        "Thời gian thu thập": BATCH2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Nguồn chưa xác nhận riêng mùa vụ tỏi tây Trung Quốc; preservation hiện tại có khả năng sai cho hàng tươi.",
        "Reviewer Notes": "Cần chứng từ nhập khẩu hoặc thông tin nhà cung cấp trước khi dùng cho seasonality.",
    },
    121: {
        "Review Status": "Partially verified - batch 2",
        "Verified Seasonality Summary": "Vĩnh Long/Mang Thít: khoai mỡ xuống giống sau khi thu hoạch lúa Đông Xuân, thu sau khoảng 6 tháng; có mô hình VietGAP.",
        "Seasonality Locality Count": 1,
        "Seasonality Evidence Count": 1,
        "General Evidence Count": 1,
        "Verified Origin Country": "Vietnam",
        "Verified Origin Province/Region": "Vĩnh Long",
        "Verified Domestic/Imported": "Domestic likely; catalog seed missing",
        "Verified Standard/Certification": "VietGAP context at Vĩnh Long; SKU standard unverified",
        "Verified Preservation Guidance": "Cần bổ sung từ ProductInfo/nhà cung cấp",
        "Verified Flavor Profile": "Bùi, tinh bột",
        "Verified Texture Profile": "Củ; mềm/bở sau nấu",
        "Verified Usage Profile": "Canh, chè, hấp/luộc",
        "Primary Source URL": "https://khuyennongvn.gov.vn/chuong-trinh-nganh-nong-nghiep/tai-co-cau-nganh-nong-nghiep/vinh-long-tuan-thu-ky-thuat-canh-tac--canh-dong-khoai-mo-ngay-cang-mo-rong-19139.html",
        "Secondary Source URL": "",
        "Thời gian thu thập": BATCH2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Catalog thiếu ProductInfo nên chưa xác nhận được origin/standard/pack size nội bộ.",
        "Reviewer Notes": "Cần tạo/bổ sung ProductInfo seed trong DB trước khi nhập dữ liệu chính thức.",
    },
    122: {
        "Review Status": "Partially verified - batch 2",
        "Verified Seasonality Summary": "Rau muống có thể sản xuất thủy canh tuần hoàn và thu hái nhiều lứa; nguồn chưa xác nhận vùng cung ứng cụ thể.",
        "Seasonality Locality Count": 1,
        "Seasonality Evidence Count": 1,
        "General Evidence Count": 1,
        "Verified Origin Country": "Vietnam likely; catalog seed missing",
        "Verified Origin Province/Region": "Unverified",
        "Verified Domestic/Imported": "Domestic likely; catalog seed missing",
        "Verified Standard/Certification": "Unverified",
        "Verified Preservation Guidance": "Rau lá tươi cần bảo quản mát/ẩm; cần bổ sung từ ProductInfo/nhà cung cấp",
        "Verified Flavor Profile": "Tươi, xanh",
        "Verified Texture Profile": "Giòn mềm",
        "Verified Usage Profile": "Luộc, xào, nấu canh",
        "Primary Source URL": "https://khuyennongvn.gov.vn/ky-thuat-trong-trot/quy-trinh-ky-thuat-san-xuat-rau-thuy-canh-tuan-hoan-759.html",
        "Secondary Source URL": "",
        "Thời gian thu thập": BATCH2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Catalog thiếu ProductInfo nên chưa xác nhận origin/standard/vùng cung ứng nội bộ; nguồn là quy trình thủy canh chung.",
        "Reviewer Notes": "Dòng trùng tên với ProductID 1 nhưng thiếu ProductInfo, cần rà lại duplicate/seed DB.",
    },
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--workbook", default=str(DEFAULT_WORKBOOK_PATH))
    return parser.parse_args()


def normalize_row(row):
    return tuple(None if cell == "" else cell for cell in row)


def append_unique_rows(ws, rows):
    existing = {
        normalize_row(row)
        for row in ws.iter_rows(min_row=2, values_only=True)
        if any(cell is not None and cell != "" for cell in row)
    }
    for row in rows:
        normalized = normalize_row(row)
        if normalized not in existing:
            ws.append(normalized)
            existing.add(normalized)


def update_products_review(ws):
    headers = [cell.value for cell in ws[1]]
    header_index = {header: idx + 1 for idx, header in enumerate(headers)}
    product_row_map = {}
    for row in range(2, ws.max_row + 1):
        product_id = ws.cell(row=row, column=1).value
        if product_id is not None:
            product_row_map[product_id] = row

    for product_id, updates in PRODUCT_SUMMARIES.items():
        row = product_row_map.get(product_id)
        if row is None:
            continue
        for header, value in updates.items():
            ws.cell(row=row, column=header_index[header]).value = value


def main() -> None:
    args = parse_args()
    workbook_path = Path(args.workbook)
    wb = load_workbook(workbook_path)
    append_unique_rows(wb["Seasonality Evidence"], SEASONALITY_ROWS)
    append_unique_rows(wb["Research Evidence"], RESEARCH_ROWS)
    update_products_review(wb["Products Review"])
    wb.save(workbook_path)


if __name__ == "__main__":
    main()
