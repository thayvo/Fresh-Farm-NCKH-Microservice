(function () {
    const cartView = document.getElementById("cart-view");
    if (!cartView) {
        return;
    }

    const selectionStorageKey = "freshfarm-cart-selected-keys";
    const summaryData = document.getElementById("cart-summary-data");
    const selectAll = document.getElementById("cart-select-all");
    const itemChecks = Array.from(document.querySelectorAll(".cart-item-check"));
    const itemRows = Array.from(document.querySelectorAll(".cart-item-row"));
    const qtyForms = Array.from(document.querySelectorAll(".cart-qty-form"));
    const selectedInline = document.getElementById("cart-selected-inline");
    const selectedItemCount = document.getElementById("selected-item-count");
    const selectedSubTotal = document.getElementById("selected-sub-total");
    const selectedInputsWrap = document.getElementById("selected-product-inputs");
    const checkoutBtn = document.getElementById("checkout-btn");
    const totalItems = Number(summaryData?.getAttribute("data-total-items") || itemChecks.length || 0);

    const vnd = (value) => {
        const safe = Number.isFinite(value) ? value : 0;
        return safe.toLocaleString("vi-VN") + " ₫";
    };

    const getRowMeta = (row) => {
        const lineTotal = Number(row.getAttribute("data-line-total") || 0);
        return {
            lineTotal: Number.isFinite(lineTotal) ? lineTotal : 0
        };
    };

    const loadStoredSelection = () => {
        try {
            const raw = window.localStorage.getItem(selectionStorageKey);
            if (!raw) {
                return [];
            }

            const parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed.map((x) => String(x || "").trim()).filter(Boolean) : [];
        } catch {
            return [];
        }
    };

    const saveStoredSelection = (keys) => {
        try {
            window.localStorage.setItem(selectionStorageKey, JSON.stringify(keys));
        } catch {
            // Ignore storage errors in private mode or blocked storage.
        }
    };

    const setCheckoutDisabled = (disabled) => {
        if (!(checkoutBtn instanceof HTMLButtonElement)) {
            return;
        }

        checkoutBtn.classList.toggle("disabled", disabled);
        checkoutBtn.disabled = disabled;
    };

    const updateSummaryBySelection = () => {
        let checkedCount = 0;
        let subTotal = 0;

        itemRows.forEach((row) => {
            const checkbox = row.querySelector(".cart-item-check");
            if (!(checkbox instanceof HTMLInputElement)) {
                return;
            }

            const isChecked = checkbox.checked;
            row.classList.toggle("is-unchecked", !isChecked);

            if (isChecked) {
                checkedCount += 1;
                subTotal += getRowMeta(row).lineTotal;
            }
        });

        if (selectedInline) {
            selectedInline.textContent = `Đã chọn ${checkedCount}/${totalItems} sản phẩm`;
        }

        if (selectedItemCount) {
            selectedItemCount.textContent = `${checkedCount} sản phẩm`;
        }

        if (selectedSubTotal) {
            selectedSubTotal.textContent = vnd(subTotal);
        }

        if (selectedInputsWrap) {
            selectedInputsWrap.innerHTML = "";
            const selectedKeys = [];

            itemRows.forEach((row) => {
                const checkbox = row.querySelector(".cart-item-check");
                if (!(checkbox instanceof HTMLInputElement) || !checkbox.checked) {
                    return;
                }

                const productId = Number(checkbox.getAttribute("data-product-id") || 0);
                if (!Number.isFinite(productId) || productId <= 0) {
                    return;
                }

                const input = document.createElement("input");
                input.type = "hidden";
                input.name = "selectedCartItemKeys";
                input.value = checkbox.getAttribute("data-cart-item-key") || "";
                selectedInputsWrap.appendChild(input);
                selectedKeys.push(input.value);
            });

            saveStoredSelection(selectedKeys);
        }

        if (selectAll instanceof HTMLInputElement) {
            selectAll.checked = checkedCount > 0 && checkedCount === itemChecks.length;
            selectAll.indeterminate = checkedCount > 0 && checkedCount < itemChecks.length;
        }

        setCheckoutDisabled(checkedCount === 0);
    };

    if (selectAll instanceof HTMLInputElement) {
        selectAll.addEventListener("change", () => {
            itemChecks.forEach((check) => {
                if (check instanceof HTMLInputElement) {
                    check.checked = selectAll.checked;
                }
            });
            updateSummaryBySelection();
        });
    }

    itemChecks.forEach((check) => {
        if (!(check instanceof HTMLInputElement)) {
            return;
        }

        check.addEventListener("change", updateSummaryBySelection);
    });

    qtyForms.forEach((form) => {
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        const visibleInput = form.querySelector(".cart-qty-input");
        const hiddenInput = form.querySelector(".cart-qty-hidden");
        const buttons = Array.from(form.querySelectorAll(".cart-qty-btn"));

        if (!(visibleInput instanceof HTMLInputElement) || !(hiddenInput instanceof HTMLInputElement)) {
            return;
        }

        const syncHidden = () => {
            const parsed = Number(visibleInput.value || hiddenInput.value || 1);
            const safe = Number.isFinite(parsed) ? Math.max(1, Math.round(parsed)) : 1;
            visibleInput.value = String(safe);
            hiddenInput.value = String(safe);
            return safe;
        };

        visibleInput.addEventListener("change", () => {
            syncHidden();
            form.submit();
        });

        visibleInput.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                syncHidden();
                form.submit();
            }
        });

        buttons.forEach((button) => {
            if (!(button instanceof HTMLButtonElement)) {
                return;
            }

            button.addEventListener("click", () => {
                const delta = Number(button.getAttribute("data-delta") || 0);
                const current = syncHidden();
                const next = Math.max(1, current + (Number.isFinite(delta) ? delta : 0));
                visibleInput.value = String(next);
                hiddenInput.value = String(next);
                form.submit();
            });
        });
    });

    const storedSelection = loadStoredSelection();
    if (storedSelection.length > 0) {
        const storedSet = new Set(storedSelection);
        itemChecks.forEach((check) => {
            if (!(check instanceof HTMLInputElement)) {
                return;
            }

            const key = (check.getAttribute("data-cart-item-key") || "").trim();
            check.checked = storedSet.has(key);
        });
    }

    updateSummaryBySelection();
})();
