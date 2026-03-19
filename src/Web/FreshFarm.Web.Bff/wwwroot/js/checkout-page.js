        (() => {
            const configNode = document.getElementById("checkoutPageConfig");
            const ghnConfigured = (configNode?.dataset.ghnConfigured || "false") === "true";
            const hasItems = (configNode?.dataset.hasItems || "false") === "true";
            const initialShippingFee = Number(configNode?.dataset.initialShippingFee || "0");
            const rows = Array.from(document.querySelectorAll("[data-item-row]"));
            const shippingFeeInput = document.getElementById("shippingFeeInput");
            const shippingFeeValue = document.getElementById("shippingFeeValue");
            const shippingFeeStatus = document.getElementById("shippingFeeStatus");
            const shippingFeeBreakdown = document.getElementById("shippingFeeBreakdown");
            const subtotalValue = document.getElementById("subtotalValue");
            const grandTotalValue = document.getElementById("grandTotalValue");
            const paymentMethodHidden = document.getElementById("paymentMethodHidden");
            const submitBtn = document.getElementById("submitBtn");
            const paymentOptions = document.querySelectorAll("#paymentOptions .payment-option");
            const addressModeInputs = document.querySelectorAll('input[name="addressMode"]');
            const savedAddressWrap = document.getElementById("savedAddressWrap");
            const manualAddressWrap = document.getElementById("manualAddressWrap");
            const saveAddressOptions = document.getElementById("saveAddressOptions");
            const savedAddressSelect = document.getElementById("savedAddressSelect");
            const hasSavedAddressOptions = !!(savedAddressSelect && savedAddressSelect.options.length > 0 && !savedAddressSelect.disabled);
            const shippingFullNameInput = document.getElementById("shippingFullName");
            const shippingPhoneInput = document.getElementById("shippingPhone");
            const shippingEmailInput = document.getElementById("shippingEmail");
            const shippingAddressDetailInput = document.getElementById("shippingAddressDetail");
            const shippingAddressLabel = document.getElementById("shippingAddressLabel");
            const shippingAddressHelp = document.getElementById("shippingAddressHelp");
            const shippingProvinceSelect = document.getElementById("shippingProvinceId");
            const shippingDistrictSelect = document.getElementById("shippingDistrictId");
            const shippingWardSelect = document.getElementById("shippingWardCode");
            const shippingProvinceNameInput = document.getElementById("shippingProvinceName");
            const shippingDistrictNameInput = document.getElementById("shippingDistrictName");
            const shippingWardNameInput = document.getElementById("shippingWardName");
            const shippingCommuneIdInput = document.getElementById("shippingCommuneId");

            const manualSnapshot = {
                fullName: shippingFullNameInput ? shippingFullNameInput.value : "",
                phone: shippingPhoneInput ? shippingPhoneInput.value : "",
                email: shippingEmailInput ? shippingEmailInput.value : "",
                addressDetail: shippingAddressDetailInput ? shippingAddressDetailInput.value : "",
                provinceId: shippingProvinceSelect ? shippingProvinceSelect.getAttribute("data-selected-id") || shippingProvinceSelect.value : "",
                districtId: shippingDistrictSelect ? shippingDistrictSelect.getAttribute("data-selected-id") || shippingDistrictSelect.value : "",
                wardCode: shippingWardSelect ? shippingWardSelect.getAttribute("data-selected-code") || shippingWardSelect.value : "",
                provinceName: shippingProvinceNameInput ? shippingProvinceNameInput.value : "",
                districtName: shippingDistrictNameInput ? shippingDistrictNameInput.value : "",
                wardName: shippingWardNameInput ? shippingWardNameInput.value : "",
                communeId: shippingCommuneIdInput ? shippingCommuneIdInput.value : ""
            };
            let wasSavedMode = false;
            let shippingFeeRequestVersion = 0;
            let shippingFeeLoading = false;
            const checkoutForm = document.getElementById("checkoutForm");
            const antiForgeryInput = checkoutForm instanceof HTMLFormElement
                ? checkoutForm.querySelector('input[name="__RequestVerificationToken"]')
                : null;

            const toNumber = (value, fallback = 0) => {
                const num = Number(value);
                return Number.isFinite(num) ? num : fallback;
            };

            const toVnd = (value) => toNumber(value, 0).toLocaleString("vi-VN") + " ₫";

            const buildUrl = (base, params) => {
                const url = new URL(base, window.location.origin);
                Object.entries(params || {}).forEach(([key, value]) => {
                    if (value !== undefined && value !== null && String(value).trim() !== "") {
                        url.searchParams.set(key, value);
                    }
                });
                return url.toString();
            };

            const fetchJson = async (url, options = {}) => {
                const response = await fetch(url, {
                    credentials: "same-origin",
                    ...options
                });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload) {
                    throw new Error(payload?.message || `HTTP ${response.status}`);
                }
                return payload;
            };

            const setShippingFeeStatus = (message, isError = false) => {
                if (!shippingFeeStatus) {
                    return;
                }

                shippingFeeStatus.textContent = message;
                shippingFeeStatus.classList.toggle("text-danger", !!isError);
                shippingFeeStatus.classList.toggle("text-muted", !isError);
            };

            const setShippingFeeValue = (value) => {
                const safeValue = Math.max(0, toNumber(value, 0));
                if (shippingFeeInput) {
                    shippingFeeInput.value = String(safeValue);
                }
                if (shippingFeeValue) {
                    shippingFeeValue.textContent = toVnd(safeValue);
                }
            };

            const setShippingFeeLoading = (isLoading) => {
                shippingFeeLoading = !!isLoading;
                if (submitBtn instanceof HTMLButtonElement) {
                    submitBtn.disabled = shippingFeeLoading || !hasItems;
                }

                if (shippingFeeLoading) {
                    setShippingFeeStatus("Đang tính phí vận chuyển GHN...");
                }
            };

            const escapeHtml = (value) => {
                return (value ?? "")
                    .toString()
                    .replace(/&/g, "&amp;")
                    .replace(/</g, "&lt;")
                    .replace(/>/g, "&gt;")
                    .replace(/\"/g, "&quot;")
                    .replace(/'/g, "&#39;");
            };

            const renderShippingBreakdown = (breakdowns) => {
                if (!(shippingFeeBreakdown instanceof HTMLElement)) {
                    return;
                }

                if (!Array.isArray(breakdowns) || breakdowns.length === 0) {
                    shippingFeeBreakdown.innerHTML = "";
                    return;
                }

                shippingFeeBreakdown.innerHTML = breakdowns.map((entry) => {
                    const sellerName = (entry?.sellerName || `Shop #${toNumber(entry?.sellerId, 0)}`).toString();
                    const itemCount = Math.max(0, toNumber(entry?.itemCount, 0));
                    const productCount = Math.max(0, toNumber(entry?.productCount, 0));
                    const fee = Math.max(0, toNumber(entry?.shippingFee, 0));
                    const serviceName = (entry?.serviceName || "").toString().trim();
                    const originLabel = (entry?.shippingOriginLabel || "").toString().trim();

                    return `
                        <div class="shipping-breakdown-item">
                            <div class="d-flex justify-content-between gap-3">
                                <div>
                                    <div class="shipping-breakdown-title">${escapeHtml(sellerName)}</div>
                                    <div class="small text-muted">${productCount} dòng • ${itemCount} sản phẩm${serviceName ? ` • ${escapeHtml(serviceName)}` : ""}</div>
                                    ${originLabel ? `<div class="small text-muted">Lấy hàng từ: ${escapeHtml(originLabel)}</div>` : ""}
                                </div>
                                <div class="fw-semibold">${toVnd(fee)}</div>
                            </div>
                        </div>
                    `;
                }).join("");
            };

            const getAddressMode = () => {
                const selectedModeInput = document.querySelector('input[name="addressMode"]:checked');
                return selectedModeInput ? selectedModeInput.value : "new";
            };

            const buildPreviewFormData = () => {
                const formData = new FormData();
                if (antiForgeryInput instanceof HTMLInputElement) {
                    formData.append("__RequestVerificationToken", antiForgeryInput.value);
                }

                const mode = getAddressMode();
                formData.append("AddressMode", mode);

                if (mode === "saved" && savedAddressSelect instanceof HTMLSelectElement) {
                    formData.append("SelectedAddressId", savedAddressSelect.value || "");
                }

                rows.forEach((row, index) => {
                    const productIdInput = row.querySelector(`input[name="Items[${index}].ProductId"]`);
                    const sellerIdInput = row.querySelector(`input[name="Items[${index}].SellerId"]`);
                    const sellerNameInput = row.querySelector(`input[name="Items[${index}].SellerName"]`);
                    const productNameInput = row.querySelector(`input[name="Items[${index}].ProductName"]`);
                    const cartItemKeyInput = row.querySelector(`input[name="Items[${index}].CartItemKey"]`);
                    const quantityInput = row.querySelector(`input[name="Items[${index}].Quantity"]`);
                    const unitPriceInput = row.querySelector(`input[name="Items[${index}].UnitPrice"]`);
                    const unitSymbolInput = row.querySelector(`input[name="Items[${index}].UnitSymbol"]`);

                    formData.append(`Items[${index}].ProductId`, productIdInput ? productIdInput.value : "0");
                    formData.append(`Items[${index}].SellerId`, sellerIdInput ? sellerIdInput.value : "0");
                    formData.append(`Items[${index}].SellerName`, sellerNameInput ? sellerNameInput.value : "");
                    formData.append(`Items[${index}].ProductName`, productNameInput ? productNameInput.value : "");
                    formData.append(`Items[${index}].CartItemKey`, cartItemKeyInput ? cartItemKeyInput.value : "");
                    formData.append(`Items[${index}].Quantity`, quantityInput ? quantityInput.value : "1");
                    formData.append(`Items[${index}].UnitPrice`, unitPriceInput ? unitPriceInput.value : "0");
                    formData.append(`Items[${index}].UnitSymbol`, unitSymbolInput ? unitSymbolInput.value : "");
                });

                formData.append("Shipping.ProvinceId", shippingProvinceSelect instanceof HTMLSelectElement ? shippingProvinceSelect.value : "");
                formData.append("Shipping.DistrictId", shippingDistrictSelect instanceof HTMLSelectElement ? shippingDistrictSelect.value : "");
                formData.append("Shipping.WardCode", shippingWardSelect instanceof HTMLSelectElement ? shippingWardSelect.value : "");
                formData.append("Shipping.ProvinceName", shippingProvinceNameInput ? shippingProvinceNameInput.value : "");
                formData.append("Shipping.DistrictName", shippingDistrictNameInput ? shippingDistrictNameInput.value : "");
                formData.append("Shipping.WardName", shippingWardNameInput ? shippingWardNameInput.value : "");
                formData.append("Shipping.AddressDetail", shippingAddressDetailInput ? shippingAddressDetailInput.value : "");
                formData.append("Shipping.CommuneId", shippingCommuneIdInput ? shippingCommuneIdInput.value : "");
                return formData;
            };

            const isAddressReadyForPreview = () => {
                const mode = getAddressMode();
                if (mode === "saved") {
                    return savedAddressSelect instanceof HTMLSelectElement && savedAddressSelect.value.trim() !== "";
                }

                return shippingProvinceSelect instanceof HTMLSelectElement
                    && shippingDistrictSelect instanceof HTMLSelectElement
                    && shippingWardSelect instanceof HTMLSelectElement
                    && shippingProvinceSelect.value.trim() !== ""
                    && shippingDistrictSelect.value.trim() !== ""
                    && shippingWardSelect.value.trim() !== "";
            };

            const requestShippingFeePreview = async ({ silentIfIncomplete = false } = {}) => {
                if (!ghnConfigured) {
                    setShippingFeeValue(initialShippingFee);
                    setShippingFeeStatus("GHN chưa sẵn sàng, hệ thống đang dùng phí vận chuyển mặc định.");
                    renderShippingBreakdown([]);
                    recalcTotal();
                    return;
                }

                if (!isAddressReadyForPreview()) {
                    setShippingFeeValue(0);
                    renderShippingBreakdown([]);
                    if (!silentIfIncomplete) {
                        setShippingFeeStatus("Chọn đầy đủ địa chỉ giao hàng để xem phí ship GHN.");
                    }
                    recalcTotal();
                    return;
                }

                const requestVersion = ++shippingFeeRequestVersion;
                setShippingFeeLoading(true);

                try {
                    const payload = await fetchJson("/checkout/ghn/preview-fee", {
                        method: "POST",
                        body: buildPreviewFormData()
                    });

                    if (requestVersion !== shippingFeeRequestVersion) {
                        return;
                    }

                    if (!payload.success) {
                        setShippingFeeValue(0);
                        renderShippingBreakdown([]);
                        setShippingFeeStatus(payload.message || "Chưa tính được phí vận chuyển GHN.", true);
                        recalcTotal();
                        return;
                    }

                    setShippingFeeValue(payload.shippingFee || 0);
                    renderShippingBreakdown(payload.sellerBreakdowns || []);
                    setShippingFeeStatus(payload.message || "Đã cập nhật phí vận chuyển GHN.");
                    recalcTotal();
                } catch (error) {
                    if (requestVersion !== shippingFeeRequestVersion) {
                        return;
                    }

                    setShippingFeeValue(0);
                    renderShippingBreakdown([]);
                    setShippingFeeStatus(error instanceof Error ? error.message : "Không tính được phí vận chuyển GHN.", true);
                    recalcTotal();
                } finally {
                    if (requestVersion === shippingFeeRequestVersion) {
                        setShippingFeeLoading(false);
                    }
                }
            };

            const setSelectOptions = (select, items, placeholder, selectedValue = "") => {
                if (!(select instanceof HTMLSelectElement)) {
                    return;
                }

                select.innerHTML = "";
                const placeholderOption = document.createElement("option");
                placeholderOption.value = "";
                placeholderOption.textContent = placeholder;
                select.appendChild(placeholderOption);

                items.forEach((item) => {
                    const option = document.createElement("option");
                    option.value = String(item.code || item.id || "");
                    option.textContent = item.name || "";
                    option.dataset.id = String(item.id || "");
                    option.dataset.code = String(item.code || "");
                    if (String(selectedValue) !== "" && String(option.value) === String(selectedValue)) {
                        option.selected = true;
                    }
                    select.appendChild(option);
                });
            };

            const setShippingReadonly = (isReadonly) => {
                if (shippingFullNameInput) {
                    shippingFullNameInput.readOnly = isReadonly;
                }
                if (shippingPhoneInput) {
                    shippingPhoneInput.readOnly = isReadonly;
                }
                if (shippingAddressDetailInput) {
                    shippingAddressDetailInput.readOnly = isReadonly;
                }
            };

            const setManualAddressControlsDisabled = (isDisabled) => {
                if (shippingProvinceSelect instanceof HTMLSelectElement) {
                    shippingProvinceSelect.disabled = isDisabled || !ghnConfigured;
                }
                if (shippingDistrictSelect instanceof HTMLSelectElement) {
                    shippingDistrictSelect.disabled = isDisabled || !ghnConfigured;
                }
                if (shippingWardSelect instanceof HTMLSelectElement) {
                    shippingWardSelect.disabled = isDisabled || !ghnConfigured;
                }
            };

            const setManualAddressRequirements = (isRequired) => {
                if (shippingProvinceSelect instanceof HTMLSelectElement) {
                    shippingProvinceSelect.required = isRequired && ghnConfigured;
                }
                if (shippingDistrictSelect instanceof HTMLSelectElement) {
                    shippingDistrictSelect.required = isRequired && ghnConfigured;
                }
                if (shippingWardSelect instanceof HTMLSelectElement) {
                    shippingWardSelect.required = isRequired && ghnConfigured;
                }
            };

            const captureManualSnapshot = () => {
                if (shippingFullNameInput) manualSnapshot.fullName = shippingFullNameInput.value;
                if (shippingPhoneInput) manualSnapshot.phone = shippingPhoneInput.value;
                if (shippingEmailInput) manualSnapshot.email = shippingEmailInput.value;
                if (shippingAddressDetailInput) manualSnapshot.addressDetail = shippingAddressDetailInput.value;
                if (shippingProvinceSelect instanceof HTMLSelectElement) manualSnapshot.provinceId = shippingProvinceSelect.value;
                if (shippingDistrictSelect instanceof HTMLSelectElement) manualSnapshot.districtId = shippingDistrictSelect.value;
                if (shippingWardSelect instanceof HTMLSelectElement) manualSnapshot.wardCode = shippingWardSelect.value;
                if (shippingProvinceNameInput) manualSnapshot.provinceName = shippingProvinceNameInput.value;
                if (shippingDistrictNameInput) manualSnapshot.districtName = shippingDistrictNameInput.value;
                if (shippingWardNameInput) manualSnapshot.wardName = shippingWardNameInput.value;
                if (shippingCommuneIdInput) manualSnapshot.communeId = shippingCommuneIdInput.value;
            };

            const updateProvinceHidden = () => {
                if (!(shippingProvinceSelect instanceof HTMLSelectElement) || !shippingProvinceNameInput) {
                    return;
                }
                const option = shippingProvinceSelect.options[shippingProvinceSelect.selectedIndex];
                shippingProvinceNameInput.value = option && option.value ? option.textContent.trim() : "";
            };

            const updateDistrictHidden = () => {
                if (!(shippingDistrictSelect instanceof HTMLSelectElement) || !shippingDistrictNameInput) {
                    return;
                }
                const option = shippingDistrictSelect.options[shippingDistrictSelect.selectedIndex];
                shippingDistrictNameInput.value = option && option.value ? option.textContent.trim() : "";
            };

            const updateWardHidden = () => {
                if (!(shippingWardSelect instanceof HTMLSelectElement)) {
                    return;
                }

                const option = shippingWardSelect.options[shippingWardSelect.selectedIndex];
                if (shippingWardNameInput) {
                    shippingWardNameInput.value = option && option.value ? option.textContent.trim() : "";
                }
                if (shippingCommuneIdInput) {
                    shippingCommuneIdInput.value = option && option.value ? (option.dataset.id || "") : "";
                }
            };

            const clearDistrictOptions = () => {
                setSelectOptions(shippingDistrictSelect, [], "Chọn quận/huyện sau khi chọn tỉnh/thành");
                if (shippingDistrictNameInput) {
                    shippingDistrictNameInput.value = "";
                }
            };

            const clearWardOptions = () => {
                setSelectOptions(shippingWardSelect, [], "Chọn phường/xã sau khi chọn quận/huyện");
                if (shippingWardNameInput) {
                    shippingWardNameInput.value = "";
                }
                if (shippingCommuneIdInput) {
                    shippingCommuneIdInput.value = "";
                }
            };

            const restoreManualSnapshot = async () => {
                if (shippingFullNameInput) shippingFullNameInput.value = manualSnapshot.fullName;
                if (shippingPhoneInput) shippingPhoneInput.value = manualSnapshot.phone;
                if (shippingEmailInput) shippingEmailInput.value = manualSnapshot.email;
                if (shippingAddressDetailInput) shippingAddressDetailInput.value = manualSnapshot.addressDetail;
                if (shippingProvinceNameInput) shippingProvinceNameInput.value = manualSnapshot.provinceName;
                if (shippingDistrictNameInput) shippingDistrictNameInput.value = manualSnapshot.districtName;
                if (shippingWardNameInput) shippingWardNameInput.value = manualSnapshot.wardName;
                if (shippingCommuneIdInput) shippingCommuneIdInput.value = manualSnapshot.communeId;
                if (shippingProvinceSelect instanceof HTMLSelectElement) shippingProvinceSelect.value = manualSnapshot.provinceId;

                if (!ghnConfigured) {
                    return;
                }

                if (manualSnapshot.provinceId) {
                    await loadGhnDistricts(manualSnapshot.provinceId, manualSnapshot.districtId);
                } else {
                    clearDistrictOptions();
                    clearWardOptions();
                }

                if (manualSnapshot.districtId) {
                    await loadGhnWards(manualSnapshot.districtId, manualSnapshot.wardCode);
                }
            };

            const applySavedAddressToShipping = () => {
                if (!savedAddressSelect || !shippingFullNameInput || !shippingPhoneInput || !shippingAddressDetailInput) {
                    return;
                }

                const selectedOption = savedAddressSelect.options[savedAddressSelect.selectedIndex];
                if (!selectedOption) {
                    return;
                }

                const parts = [
                    selectedOption.dataset.addressDetail || "",
                    selectedOption.dataset.ward || "",
                    selectedOption.dataset.district || "",
                    selectedOption.dataset.province || ""
                ].map((x) => x.trim()).filter((x) => x.length > 0);

                shippingFullNameInput.value = selectedOption.dataset.recipientName || "";
                shippingPhoneInput.value = selectedOption.dataset.phone || "";
                shippingAddressDetailInput.value = parts.join(", ");
                if (shippingProvinceNameInput) {
                    shippingProvinceNameInput.value = (selectedOption.dataset.province || "").trim();
                }
                if (shippingDistrictNameInput) {
                    shippingDistrictNameInput.value = (selectedOption.dataset.district || "").trim();
                }
                if (shippingWardNameInput) {
                    shippingWardNameInput.value = (selectedOption.dataset.ward || "").trim();
                }
                if (shippingCommuneIdInput) {
                    shippingCommuneIdInput.value = "";
                }
                if (shippingProvinceSelect instanceof HTMLSelectElement) {
                    shippingProvinceSelect.value = "";
                }
                if (shippingDistrictSelect instanceof HTMLSelectElement) {
                    shippingDistrictSelect.value = "";
                }
                if (shippingWardSelect instanceof HTMLSelectElement) {
                    shippingWardSelect.value = "";
                }
            };

            const loadGhnProvinces = async (selectedId = "") => {
                if (!(shippingProvinceSelect instanceof HTMLSelectElement) || !ghnConfigured) {
                    return;
                }

                const payload = await fetchJson("/checkout/ghn/provinces");
                const items = Array.isArray(payload.items) ? payload.items : [];
                setSelectOptions(shippingProvinceSelect, items, "Chọn tỉnh/thành", selectedId);
                updateProvinceHidden();
            };

            const loadGhnDistricts = async (provinceId, selectedId = "") => {
                if (!(shippingDistrictSelect instanceof HTMLSelectElement) || !ghnConfigured) {
                    return;
                }

                if (!provinceId) {
                    clearDistrictOptions();
                    clearWardOptions();
                    return;
                }

                const payload = await fetchJson(buildUrl("/checkout/ghn/districts", { provinceId }));
                const items = Array.isArray(payload.items) ? payload.items : [];
                setSelectOptions(shippingDistrictSelect, items, "Chọn quận/huyện", selectedId);
                updateDistrictHidden();
            };

            const loadGhnWards = async (districtId, selectedCode = "") => {
                if (!(shippingWardSelect instanceof HTMLSelectElement) || !ghnConfigured) {
                    return;
                }

                if (!districtId) {
                    clearWardOptions();
                    return;
                }

                const payload = await fetchJson(buildUrl("/checkout/ghn/wards", { districtId }));
                const items = Array.isArray(payload.items) ? payload.items : [];
                setSelectOptions(shippingWardSelect, items, "Chọn phường/xã", selectedCode);
                updateWardHidden();
            };

            const syncAddressModeUi = async () => {
                const selectedModeInput = document.querySelector('input[name="addressMode"]:checked');
                const mode = selectedModeInput ? selectedModeInput.value : "new";
                const useSavedMode = mode === "saved" && hasSavedAddressOptions;

                if (mode === "saved" && !hasSavedAddressOptions) {
                    const newModeInput = document.getElementById("addressModeNew");
                    if (newModeInput instanceof HTMLInputElement) {
                        newModeInput.checked = true;
                    }
                }

                if (savedAddressWrap) {
                    savedAddressWrap.style.display = useSavedMode ? "" : "none";
                }
                if (manualAddressWrap) {
                    manualAddressWrap.style.display = useSavedMode ? "none" : "";
                }
                if (saveAddressOptions) {
                    saveAddressOptions.style.display = useSavedMode ? "none" : "";
                }

                if (useSavedMode && !wasSavedMode) {
                    captureManualSnapshot();
                }

                if (useSavedMode) {
                    applySavedAddressToShipping();
                    setShippingReadonly(true);
                    setManualAddressControlsDisabled(true);
                    setManualAddressRequirements(false);
                    if (shippingAddressLabel) {
                        shippingAddressLabel.textContent = "Địa chỉ giao hàng";
                    }
                    if (shippingAddressHelp) {
                        shippingAddressHelp.textContent = "Địa chỉ đã lưu sẽ được dùng trực tiếp cho đơn hàng này.";
                    }
                } else {
                    setShippingReadonly(false);
                    setManualAddressControlsDisabled(false);
                    setManualAddressRequirements(true);
                    if (shippingAddressLabel) {
                        shippingAddressLabel.textContent = "Số nhà, tên đường";
                    }
                    if (shippingAddressHelp) {
                        shippingAddressHelp.textContent = "Hệ thống sẽ ghép địa chỉ chi tiết với phường/xã, quận/huyện và tỉnh/thành bạn đã chọn.";
                    }
                    if (wasSavedMode) {
                        await restoreManualSnapshot();
                    }
                }

                if (savedAddressSelect) {
                    savedAddressSelect.required = !!useSavedMode;
                }
                if (shippingFullNameInput) {
                    shippingFullNameInput.required = !useSavedMode;
                }
                if (shippingPhoneInput) {
                    shippingPhoneInput.required = !useSavedMode;
                }
                if (shippingAddressDetailInput) {
                    shippingAddressDetailInput.required = !useSavedMode;
                }

                wasSavedMode = !!useSavedMode;
            };

            const recalcTotal = () => {
                let subTotal = 0;

                rows.forEach((row) => {
                    const unitPrice = toNumber(row.getAttribute("data-unit-price"), 0);
                    const qtyInput = row.querySelector(".qty-input");
                    const lineTotalNode = row.querySelector(".line-total");

                    const qty = qtyInput
                        ? Math.max(1, toNumber(qtyInput.value, 1))
                        : 1;

                    if (qtyInput) {
                        qtyInput.value = String(qty);
                    }

                    const lineTotal = unitPrice * qty;
                    subTotal += lineTotal;

                    if (lineTotalNode) {
                        lineTotalNode.textContent = toVnd(lineTotal);
                    }
                });

                const shippingFee = shippingFeeInput
                    ? Math.max(0, toNumber(shippingFeeInput.value, 0))
                    : 0;

                subtotalValue.textContent = toVnd(subTotal);
                grandTotalValue.textContent = toVnd(subTotal + shippingFee);
                if (shippingFeeValue) {
                    shippingFeeValue.textContent = toVnd(shippingFee);
                }
            };

            document.addEventListener("click", (event) => {
                const target = event.target;
                if (!(target instanceof HTMLElement)) {
                    return;
                }

                const decButton = target.closest(".qty-dec");
                if (decButton) {
                    const row = decButton.closest("[data-item-row]");
                    const input = row?.querySelector(".qty-input");
                    if (input) {
                        input.value = String(Math.max(1, toNumber(input.value, 1) - 1));
                        recalcTotal();
                    }
                    return;
                }

                const incButton = target.closest(".qty-inc");
                if (incButton) {
                    const row = incButton.closest("[data-item-row]");
                    const input = row?.querySelector(".qty-input");
                    if (input) {
                        input.value = String(Math.max(1, toNumber(input.value, 1) + 1));
                        recalcTotal();
                    }
                    return;
                }

                const paymentOption = target.closest(".payment-option");
                if (paymentOption) {
                    const radio = paymentOption.querySelector('input[type="radio"]');
                    if (radio && radio.disabled) {
                        return;
                    }

                    paymentOptions.forEach((item) => item.classList.remove("active"));
                    paymentOption.classList.add("active");

                    if (radio) {
                        radio.checked = true;
                        paymentMethodHidden.value = radio.value;
                    }
                }
            });

            document.addEventListener("input", (event) => {
                const target = event.target;
                if (!(target instanceof HTMLInputElement)) {
                    return;
                }

                if (target.classList.contains("qty-input") || target.id === "shippingFeeInput") {
                    recalcTotal();
                    if (target.classList.contains("qty-input")) {
                        void requestShippingFeePreview({ silentIfIncomplete: true });
                    }
                }

                if (target.id === "shippingPhone" && !target.readOnly) {
                    target.value = target.value.replace(/\D/g, "").slice(0, 10);
                }
            });

            addressModeInputs.forEach((input) => {
                input.addEventListener("change", () => {
                    void syncAddressModeUi()
                        .then(() => requestShippingFeePreview({ silentIfIncomplete: true }));
                });
            });

            if (savedAddressSelect) {
                savedAddressSelect.addEventListener("change", () => {
                    const selectedModeInput = document.querySelector('input[name="addressMode"]:checked');
                    if (selectedModeInput && selectedModeInput.value === "saved") {
                        applySavedAddressToShipping();
                        void requestShippingFeePreview();
                    }
                });
            }

            if (shippingProvinceSelect instanceof HTMLSelectElement) {
                shippingProvinceSelect.addEventListener("change", async () => {
                    updateProvinceHidden();
                    clearWardOptions();
                    await loadGhnDistricts(shippingProvinceSelect.value, "");
                    captureManualSnapshot();
                    void requestShippingFeePreview({ silentIfIncomplete: true });
                });
            }

            if (shippingDistrictSelect instanceof HTMLSelectElement) {
                shippingDistrictSelect.addEventListener("change", async () => {
                    updateDistrictHidden();
                    await loadGhnWards(shippingDistrictSelect.value, "");
                    captureManualSnapshot();
                    void requestShippingFeePreview({ silentIfIncomplete: true });
                });
            }

            if (shippingWardSelect instanceof HTMLSelectElement) {
                shippingWardSelect.addEventListener("change", () => {
                    updateWardHidden();
                    captureManualSnapshot();
                    void requestShippingFeePreview();
                });
            }

            if (checkoutForm instanceof HTMLFormElement) {
                checkoutForm.addEventListener("submit", (event) => {
                    const selectedModeInput = document.querySelector('input[name="addressMode"]:checked');
                    const useSavedMode = !!(selectedModeInput && selectedModeInput.value === "saved" && hasSavedAddressOptions);

                    if (useSavedMode && savedAddressSelect && !savedAddressSelect.value) {
                        savedAddressSelect.setCustomValidity("Vui lòng chọn địa chỉ đã lưu.");
                    } else if (savedAddressSelect) {
                        savedAddressSelect.setCustomValidity("");
                    }

                    if (!useSavedMode && ghnConfigured) {
                        if (shippingProvinceSelect instanceof HTMLSelectElement) {
                            shippingProvinceSelect.setCustomValidity(shippingProvinceSelect.value ? "" : "Vui lòng chọn tỉnh/thành.");
                        }
                        if (shippingDistrictSelect instanceof HTMLSelectElement) {
                            shippingDistrictSelect.setCustomValidity(shippingDistrictSelect.value ? "" : "Vui lòng chọn quận/huyện.");
                        }
                        if (shippingWardSelect instanceof HTMLSelectElement) {
                            shippingWardSelect.setCustomValidity(shippingWardSelect.value ? "" : "Vui lòng chọn phường/xã.");
                        }
                    } else {
                        if (shippingProvinceSelect instanceof HTMLSelectElement) shippingProvinceSelect.setCustomValidity("");
                        if (shippingDistrictSelect instanceof HTMLSelectElement) shippingDistrictSelect.setCustomValidity("");
                        if (shippingWardSelect instanceof HTMLSelectElement) shippingWardSelect.setCustomValidity("");
                    }

                    if (!checkoutForm.checkValidity()) {
                        event.preventDefault();
                        event.stopPropagation();
                    }

                    checkoutForm.classList.add("was-validated");
                });
            }

            const initializeAddressSelectors = async () => {
                if (!ghnConfigured) {
                    return;
                }

                const initialProvinceId = manualSnapshot.provinceId || (shippingProvinceSelect instanceof HTMLSelectElement ? shippingProvinceSelect.dataset.selectedId || "" : "");
                const initialDistrictId = manualSnapshot.districtId || (shippingDistrictSelect instanceof HTMLSelectElement ? shippingDistrictSelect.dataset.selectedId || "" : "");
                const initialWardCode = manualSnapshot.wardCode || (shippingWardSelect instanceof HTMLSelectElement ? shippingWardSelect.dataset.selectedCode || "" : "");

                await loadGhnProvinces(initialProvinceId);
                if (initialProvinceId) {
                    await loadGhnDistricts(initialProvinceId, initialDistrictId);
                } else {
                    clearDistrictOptions();
                }
                if (initialDistrictId) {
                    await loadGhnWards(initialDistrictId, initialWardCode);
                } else {
                    clearWardOptions();
                }
                captureManualSnapshot();
            };

            recalcTotal();
            initializeAddressSelectors()
                .catch((error) => {
                    console.error(error);
                })
                .finally(() => {
                    void syncAddressModeUi()
                        .then(() => requestShippingFeePreview({ silentIfIncomplete: true }));
                });
        })();
