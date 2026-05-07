from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from openpyxl import load_workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.worksheet import Worksheet


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_WORKBOOK_PATH = REPO_ROOT / "output" / "spreadsheet" / "recommendation_product_data_review.xlsx"

HEADER_FILL = PatternFill("solid", fgColor="1F4E78")
SUBHEADER_FILL = PatternFill("solid", fgColor="D9EAF7")
ACCENT_FILL = PatternFill("solid", fgColor="E2F0D9")
WARNING_FILL = PatternFill("solid", fgColor="FFF2CC")
THIN_BORDER = Border(
    left=Side(style="thin", color="D0D7DE"),
    right=Side(style="thin", color="D0D7DE"),
    top=Side(style="thin", color="D0D7DE"),
    bottom=Side(style="thin", color="D0D7DE"),
)

FOREIGN_KEYWORDS = (
    "Mỹ",
    "Nhật Bản",
    "Úc",
    "Nam Mỹ",
    "Thái Lan",
    "Nam Phi",
    "New Zealand",
    "Trung Quốc",
    "Hàn Quốc",
)

PRODUCT_SUMMARY_HEADERS = [
    "ProductID",
    "CategoryName",
    "ProductName",
    "Sku",
    "Current Origin",
    "Current Standard",
    "Current Preservation",
    "Current Weight",
    "Current NearExpiryDays",
    "Current ShortDescription",
    "Derived Flavor Profile",
    "Derived Texture Profile",
    "Derived Usage Profile",
    "Current Origin Classification",
    "Recommendation Uses",
    "Proposed External Review Fields",
    "Research Priority",
    "Batch",
    "Batch Focus",
    "Review Status",
    "Verified Seasonality Summary",
    "Seasonality Locality Count",
    "Seasonality Evidence Count",
    "General Evidence Count",
    "Verified Origin Country",
    "Verified Origin Province/Region",
    "Verified Domestic/Imported",
    "Verified Standard/Certification",
    "Verified Preservation Guidance",
    "Verified Flavor Profile",
    "Verified Texture Profile",
    "Verified Usage Profile",
    "Primary Source URL",
    "Secondary Source URL",
    "Thời gian thu thập",
    "Tôi chưa chắc chắn",
    "Reviewer Notes",
]

BATCH_PLAN_HEADERS = [
    "Batch",
    "Focus",
    "Criteria",
    "Target Count",
    "Product IDs",
    "Representative Products",
]

SEASONALITY_EVIDENCE_HEADERS = [
    "Batch",
    "ProductID",
    "ProductName",
    "CategoryName",
    "Current Origin",
    "Locality Scope",
    "Locality Name",
    "Season Window Label",
    "Start Month",
    "End Month",
    "Peak Months",
    "Main Harvest Window",
    "Cultivation / Availability Notes",
    "Source Title",
    "Source Publisher",
    "Source URL",
    "Source Date",
    "Trust Level",
    "Thời gian thu thập",
    "Tôi chưa chắc chắn",
    "Reviewer Notes",
]

RESEARCH_EVIDENCE_HEADERS = [
    "Batch",
    "ProductID",
    "ProductName",
    "Field Name",
    "Verified Value / Claim",
    "Applicability / Locality",
    "Source Title",
    "Source Publisher",
    "Source URL",
    "Source Date",
    "Trust Level",
    "Thời gian thu thập",
    "Tôi chưa chắc chắn",
    "Reviewer Notes",
]


@dataclass(frozen=True)
class ProductRecord:
    product_id: int
    category_name: str
    product_name: str
    sku: str | None
    current_origin: str | None
    current_standard: str | None
    current_preservation: str | None
    current_weight: str | None
    current_near_expiry_days: str | None
    current_short_description: str | None
    derived_flavor_profile: str | None
    derived_texture_profile: str | None
    derived_usage_profile: str | None
    current_origin_classification: str | None
    recommendation_uses: str | None
    proposed_external_review_fields: str | None
    research_priority: str | None
    verified_origin_country: str | None
    verified_origin_province_region: str | None
    verified_domestic_imported: str | None
    verified_season_start_month: str | None
    verified_peak_season_months: str | None
    verified_season_end_month: str | None
    verified_standard_certification: str | None
    verified_preservation_guidance: str | None
    verified_flavor_profile: str | None
    verified_texture_profile: str | None
    verified_usage_profile: str | None
    primary_source_url: str | None
    secondary_source_url: str | None
    uncertain: str | None
    reviewer_notes: str | None


def load_products(ws: Worksheet) -> list[ProductRecord]:
    rows = list(ws.iter_rows(min_row=2, values_only=True))
    products: list[ProductRecord] = []
    for row in rows:
        if not row or row[0] is None:
            continue
        padded = list(row) + [None] * (32 - len(row))
        products.append(ProductRecord(*padded[:32]))
    return products


def assign_batch(product: ProductRecord) -> tuple[str, str]:
    origin = product.current_origin or ""
    category = product.category_name or ""
    if product.product_id in {121, 122}:
        return (
            "B2",
            "Thiếu metadata nền hoặc origin/classification chưa đủ để tra cứu ngoài ngay.",
        )

    has_foreign_origin = any(keyword in origin for keyword in FOREIGN_KEYWORDS)
    hybrid_origin = "(trồng VN)" in origin

    if category == "Trái cây" and has_foreign_origin:
        return (
            "B1",
            "Trái cây nhập khẩu hoặc ngoại nguồn, cần đối chiếu chính xác theo nguồn gốc xuất xứ.",
        )

    if has_foreign_origin or hybrid_origin:
        return (
            "B2",
            "Hàng nhập khẩu/hybrid origin/non-fruit cần nguồn gốc rất chặt và note rõ mức chắc chắn.",
        )

    if category == "Trái cây":
        return (
            "B3",
            "Trái cây nội địa và đặc sản vùng miền, ưu tiên peak season + khác biệt theo tỉnh/vùng.",
        )

    if category in {"Rau lá", "Rau ăn hoa / thân / mầm", "Rau thơm & gia vị"}:
        return (
            "B4",
            "Rau lá, rau thơm và nhóm thân/mầm; thường có quanh năm nhưng peak và vùng trồng khác nhau.",
        )

    return (
        "B5",
        "Rau ăn quả, củ/rễ, nấm và nhóm còn lại; kiểm tra seasonality/cultivation theo vùng hoặc mô hình trồng.",
    )


def style_sheet(ws: Worksheet, freeze_cell: str = "A2") -> None:
    ws.freeze_panes = freeze_cell
    ws.auto_filter.ref = ws.dimensions
    ws.sheet_view.showGridLines = False
    for cell in ws[1]:
        cell.fill = HEADER_FILL
        cell.font = Font(color="FFFFFF", bold=True)
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        cell.border = THIN_BORDER
    for row in ws.iter_rows(min_row=2):
        for cell in row:
            cell.alignment = Alignment(vertical="top", wrap_text=True)
            cell.border = THIN_BORDER


def auto_fit(ws: Worksheet, max_width: int = 42) -> None:
    for column_cells in ws.columns:
        length = 0
        for cell in column_cells:
            value = "" if cell.value is None else str(cell.value)
            length = max(length, len(value))
        ws.column_dimensions[get_column_letter(column_cells[0].column)].width = min(max(length + 2, 12), max_width)


def rewrite_products_sheet(wb, products: Iterable[ProductRecord]) -> None:
    if "Products Review" in wb.sheetnames:
        del wb["Products Review"]
    ws = wb.create_sheet("Products Review", 1)
    ws.append(PRODUCT_SUMMARY_HEADERS)

    for row_idx, product in enumerate(products, start=2):
        batch, batch_focus = assign_batch(product)
        ws.append(
            [
                product.product_id,
                product.category_name,
                product.product_name,
                product.sku,
                product.current_origin,
                product.current_standard,
                product.current_preservation,
                product.current_weight,
                product.current_near_expiry_days,
                product.current_short_description,
                product.derived_flavor_profile,
                product.derived_texture_profile,
                product.derived_usage_profile,
                product.current_origin_classification,
                product.recommendation_uses,
                product.proposed_external_review_fields,
                product.research_priority,
                batch,
                batch_focus,
                "Pending",
                None,
                f"=COUNTIF('Seasonality Evidence'!B:B,A{row_idx})",
                f"=COUNTIF('Seasonality Evidence'!B:B,A{row_idx})",
                f"=COUNTIF('Research Evidence'!B:B,A{row_idx})",
                product.verified_origin_country,
                product.verified_origin_province_region,
                product.verified_domestic_imported,
                product.verified_standard_certification,
                product.verified_preservation_guidance,
                product.verified_flavor_profile,
                product.verified_texture_profile,
                product.verified_usage_profile,
                product.primary_source_url,
                product.secondary_source_url,
                None,
                product.uncertain,
                product.reviewer_notes,
            ]
        )

        if product.product_id in {121, 122}:
            ws[f"T{row_idx}"] = "Blocked - thiếu ProductInfo seed"
        elif batch == "B1":
            ws[f"T{row_idx}"] = "Ready for batch 1"

    style_sheet(ws)
    for cell in ws["V"]:
        if cell.row == 1:
            cell.fill = SUBHEADER_FILL
            cell.font = Font(bold=True)
            cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
    for col in ("R", "S", "T", "U", "AJ", "AI"):
        for cell in ws[col]:
            if cell.row == 1:
                cell.fill = SUBHEADER_FILL
                cell.font = Font(bold=True)
                cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
    auto_fit(ws)


def rebuild_scope_sheet(wb, products: list[ProductRecord]) -> None:
    if "Scope" in wb.sheetnames:
        del wb["Scope"]
    ws = wb.create_sheet("Scope", 0)
    rows = [
        ["Recommendation Product Data Review Scope", ""],
        ["Current workbook status", "Prepared for multi-batch catalog research with locality-aware seasonality evidence."],
        ["Catalog product count", len(products)],
        ["Products missing ProductInfo in seed", "121, 122"],
        ["Research policy - domestic", "Only use extremely trustworthy Vietnam sources."],
        ["Research policy - imported", "Prefer origin-country primary/industry sources or high-trust international references."],
        ["Seasonality rule", "Do not compress to a single generic season when a product has multiple windows or locality-specific timing."],
        ["Uncertainty rule", "Populate the column `Tôi chưa chắc chắn` whenever sources are missing, conflicting, or only partially applicable."],
        ["Timestamp rule", "Every researched product/evidence row must store `Thời gian thu thập` in a clear datetime format."],
        ["Workbook layout", "Products Review = summary per product; Seasonality Evidence = locality/window rows; Research Evidence = non-seasonality claims; Batch Plan = execution order."],
        ["Current recommendation caveat", "Application code still uses heuristic seasonality based on month + keyword matching, not authoritative crop calendars."],
    ]
    for row in rows:
        ws.append(row)

    ws["A1"].fill = HEADER_FILL
    ws["A1"].font = Font(color="FFFFFF", bold=True)
    ws["A1"].alignment = Alignment(horizontal="center", vertical="center")
    ws["B1"].fill = HEADER_FILL
    ws["B1"].font = Font(color="FFFFFF", bold=True)
    ws["B1"].alignment = Alignment(horizontal="center", vertical="center")
    for row in ws.iter_rows():
        for cell in row:
            cell.border = THIN_BORDER
            cell.alignment = Alignment(vertical="top", wrap_text=True)
    ws.column_dimensions["A"].width = 34
    ws.column_dimensions["B"].width = 108
    ws.sheet_view.showGridLines = False


def rewrite_field_matrix_sheet(wb) -> None:
    if "Field Matrix" in wb.sheetnames:
        del wb["Field Matrix"]
    ws = wb.create_sheet("Field Matrix")
    ws.append(["Field", "Current system usage", "Priority", "Why it matters", "Review guidance"])
    rows = [
        ["Origin", "Directly used in recommendation scoring/reasons.", "High", "Supports locality, region-fit and seasonal explanations.", "Verify country + province/region from trusted origin sources."],
        ["Standard", "Used in content quality score.", "High", "Signals trust and product confidence.", "Only fill when an official or seller-backed standard can be verified."],
        ["Preservation", "Used in content quality score.", "High", "Impacts buyer guidance and freshness expectations.", "Prefer official product/commodity handling guidance from authoritative sources."],
        ["Weight", "Used in content quality score.", "Medium", "May affect product comparability, but can be pack-size specific.", "Review against internal seller data first; external web may not reflect exact pack size."],
        ["Flavor profile", "Currently derived heuristically from text.", "Medium", "Can improve explanation quality.", "Only normalize when product-specific descriptors are supported by trusted commodity references."],
        ["Texture profile", "Currently derived heuristically from text.", "Medium", "Can improve recommendation reasons.", "Keep generic unless supported by authoritative product references."],
        ["Usage profile", "Currently derived heuristically from text.", "Medium", "Can support better buyer-facing suggestions.", "Prefer broad, well-supported culinary uses over subjective claims."],
        ["Seasonality by locality", "Not stored authoritatively today; current app uses heuristic month+keyword matching.", "Critical", "Main gap for `đúng mùa` recommendations.", "Record one evidence row per locality/window; do not collapse multiple windows into one."],
        ["Peak harvest windows", "Not modeled directly.", "Critical", "Allows stronger `đúng mùa` ranking and explanation.", "Capture peak months separately from wider availability windows."],
        ["Domestic/Imported", "Inferred loosely from origin text today.", "High", "Changes source policy and recommendation trust.", "Normalize based on verified origin, not naming alone."],
    ]
    for row in rows:
        ws.append(row)
    style_sheet(ws)
    auto_fit(ws, max_width=56)


def rewrite_batch_plan_sheet(wb, products: list[ProductRecord]) -> None:
    if "Batch Plan" in wb.sheetnames:
        del wb["Batch Plan"]
    ws = wb.create_sheet("Batch Plan")
    ws.append(BATCH_PLAN_HEADERS)

    batch_groups: dict[str, list[ProductRecord]] = {"B1": [], "B2": [], "B3": [], "B4": [], "B5": []}
    batch_focus: dict[str, str] = {}
    batch_criteria = {
        "B1": "Imported fruits or foreign-origin fruits requiring origin-country sourcing first.",
        "B2": "Hybrid-origin/imported non-fruit items or rows blocked by missing seed metadata.",
        "B3": "Domestic fruits and regional specialties with strong locality-sensitive seasonality.",
        "B4": "Leafy greens, herbs, stems and sprouts; often year-round but peak differs by region.",
        "B5": "Domestic fruiting vegetables, roots/tubers, mushrooms and remaining products.",
    }
    for product in products:
        batch, focus = assign_batch(product)
        batch_groups[batch].append(product)
        batch_focus[batch] = focus

    for batch_code in ("B1", "B2", "B3", "B4", "B5"):
        batch_products = batch_groups[batch_code]
        ws.append(
            [
                batch_code,
                batch_focus.get(batch_code),
                batch_criteria[batch_code],
                len(batch_products),
                ", ".join(str(item.product_id) for item in batch_products),
                ", ".join(item.product_name for item in batch_products[:8]) + (" ..." if len(batch_products) > 8 else ""),
            ]
        )

    style_sheet(ws)
    auto_fit(ws, max_width=72)


def ensure_empty_sheet(wb, title: str, headers: list[str]) -> None:
    if title in wb.sheetnames:
        del wb[title]
    ws = wb.create_sheet(title)
    ws.append(headers)
    style_sheet(ws)
    if title == "Seasonality Evidence":
        ws["A1"].fill = ACCENT_FILL
        ws["A1"].font = Font(bold=True)
    if title == "Research Evidence":
        ws["A1"].fill = WARNING_FILL
        ws["A1"].font = Font(bold=True)
    auto_fit(ws, max_width=48)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--input",
        dest="input_path",
        default=str(DEFAULT_WORKBOOK_PATH),
        help="Existing workbook to transform.",
    )
    parser.add_argument(
        "--output",
        dest="output_path",
        default=str(DEFAULT_WORKBOOK_PATH),
        help="Destination workbook path.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    input_path = Path(args.input_path)
    output_path = Path(args.output_path)

    wb = load_workbook(input_path)
    source_ws = wb["Products Review"]
    products = load_products(source_ws)

    rebuild_scope_sheet(wb, products)
    rewrite_products_sheet(wb, products)
    rewrite_field_matrix_sheet(wb)
    rewrite_batch_plan_sheet(wb, products)
    ensure_empty_sheet(wb, "Seasonality Evidence", SEASONALITY_EVIDENCE_HEADERS)
    ensure_empty_sheet(wb, "Research Evidence", RESEARCH_EVIDENCE_HEADERS)

    ordered_titles = [
        "Scope",
        "Batch Plan",
        "Products Review",
        "Seasonality Evidence",
        "Research Evidence",
        "Field Matrix",
    ]
    wb._sheets = [wb[title] for title in ordered_titles]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    wb.save(output_path)


if __name__ == "__main__":
    main()
