from __future__ import annotations

import argparse
from pathlib import Path

from openpyxl import load_workbook


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_WORKBOOK_PATH = REPO_ROOT / "output" / "spreadsheet" / "recommendation_product_data_review_multibatch.xlsx"
ROUND2_COLLECTION_TIMESTAMP = "2026-04-24 14:48:34 ICT"


SEASONALITY_ROWS = [
    (
        "B1",
        68,
        "Chuối già Nam Mỹ",
        "Trái cây",
        "Nam Mỹ",
        "Regional tropical belt",
        "South America (exact country not yet confirmed)",
        "Perennial harvest window",
        1,
        12,
        "Year-round",
        "No",
        "FAO describes bananas and plantains as perennial crops that grow quickly and can be harvested all year round in tropical regions.",
        "The World Banana Economy, 1985-2002",
        "FAO",
        "https://www.fao.org/3/y5102e/y5102e04.htm",
        "",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "Catalog chỉ ghi Nam Mỹ, chưa xác định quốc gia xuất xứ thực tế của lô hàng.",
        "Dùng nguồn FAO để xác nhận đặc tính mùa vụ chung; chưa đủ để chốt vùng trồng cụ thể.",
    ),
    (
        "B3",
        64,
        "Xoài cát Hòa Lộc",
        "Trái cây",
        "Tiền Giang, Việt Nam",
        "District / GI area",
        "Cái Bè, Tiền Giang",
        "Mùa vụ GI truyền thống",
        2,
        5,
        "3-5",
        "Yes",
        "Tài liệu chỉ dẫn địa lý của Cục Sở hữu trí tuệ ghi mùa vụ thu hoạch từ tháng 2 đến tháng 5 dương lịch.",
        "Chỉ dẫn địa lý – Di sản thiên nhiên và văn hóa Việt Nam",
        "Cục Sở hữu trí tuệ",
        "https://ipvietnam.gov.vn/documents/20195/811934/Ch%E1%BB%89%2Bd%E1%BA%ABn%2B%C4%91%E1%BB%8Ba%2Bl%C3%BD%2B%E2%80%93%2BDi%2Bs%E1%BA%A3n%2Bthi%C3%AAn%2Bnhi%C3%AAn%2Bv%C4%83n%2Bh%C3%B3a%2BVi%E1%BB%87t%2B%28ti%E1%BA%BFng%2BVi%E1%BB%87t%29.pdf/a56b0cb4-d8bd-475a-be64-bba40329fb9f",
        "",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "",
        "",
    ),
    (
        "B3",
        64,
        "Xoài cát Hòa Lộc",
        "Trái cây",
        "Tiền Giang, Việt Nam",
        "District / GI area",
        "Cái Bè, Tiền Giang",
        "Mùa vụ tăng thêm",
        10,
        12,
        "10-12",
        "No",
        "Bộ Công Thương ghi nhận hiện nay người trồng đã tăng thêm mùa vụ từ tháng 10 đến tháng 12 dương lịch.",
        "Xoài cát Hòa Lộc nức lòng giới sành ăn",
        "Bộ Công Thương",
        "https://moit.gov.vn/tu-hao-hang-viet-nam/gioi-thieu-xoai-cat-hoa-loc.html",
        "2022-09-08",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "",
        "Đây là mùa vụ mở rộng/rải vụ, không phải cửa sổ GI truyền thống.",
    ),
    (
        "B3",
        70,
        "Thanh long ruột đỏ",
        "Trái cây",
        "Bình Thuận, Việt Nam",
        "Province / GI area",
        "Bình Thuận",
        "Vụ nghịch cuối năm - đầu năm",
        "10 âm lịch",
        "sau Tết",
        "tháng Chạp - sau Tết",
        "No",
        "Sở NN&PTNT Bình Thuận ghi nhận nông dân bắt đầu chong đèn từ tháng 10 âm lịch và các lứa nghịch vụ thu hoạch từ tháng Chạp trở đi để phục vụ thị trường trước và sau Tết.",
        "Thanh long vào vụ nghịch: [Bài 1] Nâng cao chất lượng đáp ứng thị trường",
        "Sở NN&PTNT Bình Thuận",
        "https://snnptnt.binhthuan.gov.vn/tin-nong-nghiep/thanh-long-vao-vu-nghich-bai-1-nang-cao-chat-luong-dap-ung-thi-truong-867366",
        "2023-12-05",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "Mốc tháng đang ở dạng âm lịch theo nguồn gốc, không ép đổi sang dương lịch để tránh sai lệch theo từng năm.",
        "Nguồn hiện tại mới xác nhận chắc phần vụ nghịch.",
    ),
]


RESEARCH_ROWS = [
    (
        "B1",
        68,
        "Chuối già Nam Mỹ",
        "Origin scope",
        "Catalog origin is only South America; exact country/province remains unconfirmed.",
        "Origin verification",
        "The World Banana Economy, 1985-2002",
        "FAO",
        "https://www.fao.org/3/y5102e/y5102e04.htm",
        "",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "Chưa có nguồn đủ mạnh để chốt quốc gia xuất xứ cho SKU này.",
        "",
    ),
    (
        "B1",
        68,
        "Chuối già Nam Mỹ",
        "Possible source-country context",
        "If the lot is from Ecuador, the largest production concentrations include Los Ríos, Guayas and El Oro.",
        "Context only - not assigned to this SKU",
        "Boletín situacional de banano 2023",
        "Ministerio de Agricultura y Ganadería del Ecuador",
        "https://sipa.agricultura.gob.ec/boletines/situacionales/2023/boletin_situacional_banano_2023.pdf",
        "2024",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "Chỉ dùng làm bối cảnh vùng trồng nếu sau này xác nhận được nguồn hàng là Ecuador.",
        "",
    ),
    (
        "B3",
        64,
        "Xoài cát Hòa Lộc",
        "Origin province/region",
        "Huyện Cái Bè, tỉnh Tiền Giang; khu vực GI gồm 13 xã.",
        "Origin verification",
        "Bảo hộ chỉ dẫn địa lý “Hòa Lộc” cho sản phẩm xoài cát",
        "Cục Sở hữu trí tuệ",
        "https://www.ipvietnam.gov.vn/web/english/statistic/-/asset_publisher/lkBHulAdnfEF/content/bao-ho-chi-dan-ia-ly-hoa-loc-cho-san-pham-xoai-cat",
        "2009-12-01",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "",
        "",
    ),
    (
        "B3",
        64,
        "Xoài cát Hòa Lộc",
        "Standard/certification context",
        "Đã được cấp giấy chứng nhận sản xuất an toàn theo tiêu chuẩn GlobalGAP.",
        "Production/quality context",
        "Xoài cát Hòa Lộc nức lòng giới sành ăn",
        "Bộ Công Thương",
        "https://moit.gov.vn/tu-hao-hang-viet-nam/gioi-thieu-xoai-cat-hoa-loc.html",
        "2022-09-08",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "Nguồn nói ở mức vùng sản xuất/xoài cát Hòa Lộc, không khẳng định mọi lô thương mại riêng lẻ đều đang giữ chứng nhận hiệu lực tại thời điểm hiện tại.",
        "",
    ),
    (
        "B3",
        64,
        "Xoài cát Hòa Lộc",
        "Flavor / texture",
        "Thịt dẻo, mịn, dày, ít xơ, rất ngọt và mùi thơm dịu.",
        "Commodity description",
        "Xoài cát Hòa Lộc nức lòng giới sành ăn",
        "Bộ Công Thương",
        "https://moit.gov.vn/tu-hao-hang-viet-nam/gioi-thieu-xoai-cat-hoa-loc.html",
        "2022-09-08",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "",
        "",
    ),
    (
        "B3",
        70,
        "Thanh long ruột đỏ",
        "Origin province/region",
        "Khu vực GI thanh long Bình Thuận gồm Hàm Tân, Hàm Thuận Nam, Hàm Thuận Bắc, Bắc Bình và TP Phan Thiết.",
        "Origin verification",
        "Nhật Bản bảo hộ chỉ dẫn địa lý cho sản phẩm thanh long Bình Thuận",
        "Cục Sở hữu trí tuệ",
        "https://ipvietnam.gov.vn/phat-trien-chi-dan-ia-ly/-/asset_publisher/SGA9PgvmYtWI/content/thanh-long-binh-thuan-duoc-bao-ho-cddl-tai-nhat-ban?inheritRedirect=false",
        "",
        "High",
        ROUND2_COLLECTION_TIMESTAMP,
        "Nguồn GI đang ở mức quả thanh long Bình Thuận nói chung, chưa tách riêng ruột đỏ.",
        "",
    ),
]


PRODUCT_SUMMARIES = {
    68: {
        "Review Status": "Partially verified - batch 1",
        "Verified Seasonality Summary": "Chuối vùng nhiệt đới Nam Mỹ có đặc tính thu hoạch quanh năm; tuy vậy SKU hiện chỉ ghi Nam Mỹ nên chưa xác nhận được quốc gia/vùng trồng cụ thể.",
        "Verified Domestic/Imported": "Imported",
        "Primary Source URL": "https://www.fao.org/3/y5102e/y5102e04.htm",
        "Secondary Source URL": "https://sipa.agricultura.gob.ec/boletines/situacionales/2023/boletin_situacional_banano_2023.pdf",
        "Thời gian thu thập": ROUND2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Chưa thể chốt country/province vì catalog mới ghi Nam Mỹ; dữ liệu vùng Ecuador chỉ mang tính bối cảnh nếu sau này xác nhận được nguồn hàng.",
    },
    64: {
        "Review Status": "Partially verified - batch 3",
        "Verified Seasonality Summary": "Tiền Giang (Cái Bè): mùa vụ GI truyền thống từ tháng 2-5; nguồn Bộ Công Thương ghi nhận hiện nay có thêm mùa vụ từ tháng 10-12.",
        "Verified Origin Country": "Vietnam",
        "Verified Origin Province/Region": "Tiền Giang - Cái Bè",
        "Verified Domestic/Imported": "Domestic",
        "Verified Standard/Certification": "GlobalGAP (context at vùng/nhóm sản phẩm; cần kiểm tra hiệu lực nếu dùng như claim bắt buộc cho từng lô)",
        "Verified Flavor Profile": "Ngọt, thơm dịu",
        "Verified Texture Profile": "Dẻo, mịn, ít xơ",
        "Primary Source URL": "https://ipvietnam.gov.vn/documents/20195/811934/Ch%E1%BB%89%2Bd%E1%BA%ABn%2B%C4%91%E1%BB%8Ba%2Bl%C3%BD%2B%E2%80%93%2BDi%2Bs%E1%BA%A3n%2Bthi%C3%AAn%2Bnhi%C3%AAn%2Bv%C4%83n%2Bh%C3%B3a%2BVi%E1%BB%87t%2B%28ti%E1%BA%BFng%2BVi%E1%BB%87t%29.pdf/a56b0cb4-d8bd-475a-be64-bba40329fb9f",
        "Secondary Source URL": "https://moit.gov.vn/tu-hao-hang-viet-nam/gioi-thieu-xoai-cat-hoa-loc.html",
        "Thời gian thu thập": ROUND2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Khung 10-12 là mùa vụ tăng thêm theo nguồn Bộ Công Thương; nếu cần áp vào rule máy nên tách rõ `mùa truyền thống` và `mùa mở rộng/rải vụ`.",
    },
    70: {
        "Review Status": "Partially verified - batch 3",
        "Verified Seasonality Summary": "Bình Thuận: vùng GI gồm Hàm Tân, Hàm Thuận Nam, Hàm Thuận Bắc, Bắc Bình và Phan Thiết; vụ nghịch phục vụ cuối năm - Tết bắt đầu chong đèn từ tháng 10 âm lịch, thu hoạch từ tháng Chạp trở đi.",
        "Verified Origin Country": "Vietnam",
        "Verified Origin Province/Region": "Bình Thuận - vùng GI thanh long",
        "Verified Domestic/Imported": "Domestic",
        "Primary Source URL": "https://ipvietnam.gov.vn/phat-trien-chi-dan-ia-ly/-/asset_publisher/SGA9PgvmYtWI/content/thanh-long-binh-thuan-duoc-bao-ho-cddl-tai-nhat-ban?inheritRedirect=false",
        "Secondary Source URL": "https://snnptnt.binhthuan.gov.vn/tin-nong-nghiep/thanh-long-vao-vu-nghich-bai-1-nang-cao-chat-luong-dap-ung-thi-truong-867366",
        "Thời gian thu thập": ROUND2_COLLECTION_TIMESTAMP,
        "Tôi chưa chắc chắn": "Nguồn hiện mới chắc ở mức thanh long Bình Thuận nói chung và vụ nghịch theo âm lịch; chưa có nguồn mạnh cùng lúc tách riêng cho biến thể ruột đỏ và cửa sổ vụ thuận theo tháng dương lịch.",
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
