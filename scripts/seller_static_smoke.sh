#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SELLER_CONTROLLERS="$ROOT_DIR/src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers"
SELLER_VIEWS="$ROOT_DIR/src/Web/FreshFarm.Web.Bff/Areas/Seller/Views"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "[seller-static-smoke] ROOT: $ROOT_DIR"

LEGACY_PATTERN='FreshFram|FreshFarmDBEntities|System\.Data\.Entity|System\.Web\.Mvc|IHtmlString|Scripts\.Render|Styles\.Render|AdminAuthorize|NoCache'
rg -n "$LEGACY_PATTERN" "$SELLER_CONTROLLERS" "$SELLER_VIEWS" --glob '!**/web.config*' > "$TMP_DIR/legacy_hits.txt" || true

find "$SELLER_VIEWS" -name '*.cshtml' -print0 \
  | xargs -0 perl -ne '
      while (/Url\.Action\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g) { print "$2|$1\n"; }
      while (/Html\.BeginForm\(\s*"([^"]+)"\s*,\s*"([^"]+)"/g) { print "$2|$1\n"; }
    ' \
  | sort -u > "$TMP_DIR/view_calls_all.txt"

grep -v '^Account|' "$TMP_DIR/view_calls_all.txt" > "$TMP_DIR/view_calls.txt" || true

find "$SELLER_CONTROLLERS" -name '*Controller.cs' -print0 \
  | xargs -0 perl -ne '
      if (/class\s+(\w+)Controller/) { $c = $1; }
      while (/public\s+(?:async\s+)?[A-Za-z_][A-Za-z0-9_<>\[\],\.\?]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(/g) {
        $m = $1;
        print "$c|$m\n" if defined $c;
      }
    ' \
  | sort -u > "$TMP_DIR/controller_actions.txt"

comm -23 "$TMP_DIR/view_calls.txt" "$TMP_DIR/controller_actions.txt" > "$TMP_DIR/missing_calls.txt" || true

rg -n '^\s*@page\b' "$SELLER_VIEWS" --glob '*.cshtml' > "$TMP_DIR/page_directive_hits.txt" || true

LEGACY_COUNT="$(wc -l < "$TMP_DIR/legacy_hits.txt" | tr -d ' ')"
MISSING_COUNT="$(wc -l < "$TMP_DIR/missing_calls.txt" | tr -d ' ')"
PAGE_DIRECTIVE_COUNT="$(wc -l < "$TMP_DIR/page_directive_hits.txt" | tr -d ' ')"

echo
echo "[seller-static-smoke] Summary"
echo "  - Legacy pattern hits: $LEGACY_COUNT"
echo "  - Missing explicit route calls: $MISSING_COUNT"
echo "  - @page directive hits in Seller views: $PAGE_DIRECTIVE_COUNT"

FAILED=0

if [[ "$LEGACY_COUNT" != "0" ]]; then
  FAILED=1
  echo
  echo "[seller-static-smoke] Legacy hits"
  cat "$TMP_DIR/legacy_hits.txt"
fi

if [[ "$MISSING_COUNT" != "0" ]]; then
  FAILED=1
  echo
  echo "[seller-static-smoke] Missing calls (Controller|Action)"
  cat "$TMP_DIR/missing_calls.txt"
fi

if [[ "$PAGE_DIRECTIVE_COUNT" != "0" ]]; then
  FAILED=1
  echo
  echo "[seller-static-smoke] Unexpected @page directives"
  cat "$TMP_DIR/page_directive_hits.txt"
fi

if [[ "$FAILED" == "1" ]]; then
  echo
  echo "[seller-static-smoke] FAIL"
  exit 1
fi

echo
echo "[seller-static-smoke] PASS"
