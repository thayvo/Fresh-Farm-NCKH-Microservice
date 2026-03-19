        (() => {
            const configNode = document.getElementById("profilePageConfig");
            const ghnConfigured = (configNode?.dataset.ghnConfigured || "false") === "true";

            if (!ghnConfigured) {
                return;
            }

            const buildUrl = (base, params) => {
                const url = new URL(base, window.location.origin);
                Object.entries(params || {}).forEach(([key, value]) => {
                    if (value !== undefined && value !== null && String(value).trim() !== "") {
                        url.searchParams.set(key, value);
                    }
                });
                return url.toString();
            };

            const fetchJson = async (url) => {
                const response = await fetch(url, { credentials: "same-origin" });
                const payload = await response.json().catch(() => null);
                if (!response.ok || !payload) {
                    throw new Error(payload?.message || `HTTP ${response.status}`);
                }
                return payload;
            };

            const normalizeText = (value) => (value || "").toString().trim().toLowerCase();

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
                    option.value = String(item.id || "");
                    option.textContent = item.name || "";
                    option.dataset.code = String(item.code || "");
                    if (String(selectedValue) !== "" && String(option.value) === String(selectedValue)) {
                        option.selected = true;
                    }
                    select.appendChild(option);
                });
            };

            const matchOptionByName = (select, name) => {
                if (!(select instanceof HTMLSelectElement) || !name) {
                    return "";
                }

                const normalized = normalizeText(name);
                const option = Array.from(select.options).find(item => normalizeText(item.textContent) === normalized);
                return option ? option.value : "";
            };

            const updateHiddenNames = (form) => {
                const provinceSelect = form.querySelector(".ghn-province-select");
                const districtSelect = form.querySelector(".ghn-district-select");
                const wardSelect = form.querySelector(".ghn-ward-select");
                const provinceNameInput = form.querySelector(".ghn-province-name");
                const districtNameInput = form.querySelector(".ghn-district-name");
                const wardNameInput = form.querySelector(".ghn-ward-name");

                if (provinceSelect instanceof HTMLSelectElement && provinceNameInput instanceof HTMLInputElement) {
                    const provinceOption = provinceSelect.options[provinceSelect.selectedIndex];
                    provinceNameInput.value = provinceOption && provinceOption.value ? provinceOption.textContent.trim() : "";
                }

                if (districtSelect instanceof HTMLSelectElement && districtNameInput instanceof HTMLInputElement) {
                    const districtOption = districtSelect.options[districtSelect.selectedIndex];
                    districtNameInput.value = districtOption && districtOption.value ? districtOption.textContent.trim() : "";
                }

                if (wardSelect instanceof HTMLSelectElement && wardNameInput instanceof HTMLInputElement) {
                    const wardOption = wardSelect.options[wardSelect.selectedIndex];
                    wardNameInput.value = wardOption && wardOption.value ? wardOption.textContent.trim() : "";
                }
            };

            const loadDistricts = async (form, provinceId, selectedDistrictId = "", selectedDistrictName = "") => {
                const districtSelect = form.querySelector(".ghn-district-select");
                const wardSelect = form.querySelector(".ghn-ward-select");
                const districtNameInput = form.querySelector(".ghn-district-name");
                const wardNameInput = form.querySelector(".ghn-ward-name");

                if (!(districtSelect instanceof HTMLSelectElement) || !(wardSelect instanceof HTMLSelectElement)) {
                    return;
                }

                setSelectOptions(wardSelect, [], "Chọn quận/huyện trước");
                if (wardNameInput instanceof HTMLInputElement) {
                    wardNameInput.value = "";
                }

                if (!provinceId) {
                    setSelectOptions(districtSelect, [], "Chọn tỉnh/thành trước");
                    if (districtNameInput instanceof HTMLInputElement) {
                        districtNameInput.value = "";
                    }
                    return;
                }

                const payload = await fetchJson(buildUrl("/account/profile/ghn/districts", { provinceId }));
                const items = Array.isArray(payload.items) ? payload.items : [];
                setSelectOptions(districtSelect, items, "Chọn quận/huyện", selectedDistrictId);

                if (!districtSelect.value && selectedDistrictName) {
                    districtSelect.value = matchOptionByName(districtSelect, selectedDistrictName);
                }

                updateHiddenNames(form);
            };

            const loadWards = async (form, districtId, selectedWardId = "", selectedWardName = "") => {
                const wardSelect = form.querySelector(".ghn-ward-select");
                if (!(wardSelect instanceof HTMLSelectElement)) {
                    return;
                }

                if (!districtId) {
                    setSelectOptions(wardSelect, [], "Chọn quận/huyện trước");
                    updateHiddenNames(form);
                    return;
                }

                const payload = await fetchJson(buildUrl("/account/profile/ghn/wards", { districtId }));
                const items = Array.isArray(payload.items) ? payload.items : [];
                setSelectOptions(wardSelect, items, "Chọn phường/xã", selectedWardId);

                if (!wardSelect.value && selectedWardName) {
                    wardSelect.value = matchOptionByName(wardSelect, selectedWardName);
                }

                updateHiddenNames(form);
            };

            const initializeAddressForm = async (form) => {
                const provinceSelect = form.querySelector(".ghn-province-select");
                const districtSelect = form.querySelector(".ghn-district-select");
                const wardSelect = form.querySelector(".ghn-ward-select");
                if (!(provinceSelect instanceof HTMLSelectElement) || !(districtSelect instanceof HTMLSelectElement) || !(wardSelect instanceof HTMLSelectElement)) {
                    return;
                }

                const provinceNameInput = form.querySelector(".ghn-province-name");
                const districtNameInput = form.querySelector(".ghn-district-name");
                const wardNameInput = form.querySelector(".ghn-ward-name");

                const provincePayload = await fetchJson("/account/profile/ghn/provinces");
                const provinceItems = Array.isArray(provincePayload.items) ? provincePayload.items : [];
                setSelectOptions(provinceSelect, provinceItems, "Chọn tỉnh/thành", provinceSelect.dataset.selectedId || "");

                if (!provinceSelect.value && provinceSelect.dataset.selectedName) {
                    provinceSelect.value = matchOptionByName(provinceSelect, provinceSelect.dataset.selectedName);
                }

                updateHiddenNames(form);

                const districtName = districtSelect.dataset.selectedName || (districtNameInput instanceof HTMLInputElement ? districtNameInput.value : "");
                const wardName = wardSelect.dataset.selectedName || (wardNameInput instanceof HTMLInputElement ? wardNameInput.value : "");

                await loadDistricts(form, provinceSelect.value, districtSelect.dataset.selectedId || "", districtName);
                await loadWards(form, districtSelect.value, wardSelect.dataset.selectedId || "", wardName);

                provinceSelect.required = true;
                districtSelect.required = true;
                wardSelect.required = true;

                provinceSelect.addEventListener("change", async () => {
                    await loadDistricts(form, provinceSelect.value);
                });

                districtSelect.addEventListener("change", async () => {
                    await loadWards(form, districtSelect.value);
                });

                wardSelect.addEventListener("change", () => {
                    updateHiddenNames(form);
                });
            };

            document.querySelectorAll("form[data-ghn-form='true']").forEach((form) => {
                initializeAddressForm(form).catch((error) => {
                    console.error(error);
                });
            });
        })();
