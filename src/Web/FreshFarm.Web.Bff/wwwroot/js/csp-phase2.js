(function () {
    const initBootstrapValidation = () => {
        document.querySelectorAll(".needs-validation").forEach((form) => {
            if (!(form instanceof HTMLFormElement) || form.dataset.validationBound === "true") {
                return;
            }

            form.dataset.validationBound = "true";
            form.addEventListener("submit", (event) => {
                if (!form.checkValidity()) {
                    event.preventDefault();
                    event.stopPropagation();
                }

                form.classList.add("was-validated");
            });
        });
    };

    const initConfirmSubmissions = () => {
        document.querySelectorAll("form[data-confirm-message]").forEach((form) => {
            if (!(form instanceof HTMLFormElement) || form.dataset.confirmBound === "true") {
                return;
            }

            form.dataset.confirmBound = "true";
            form.addEventListener("submit", (event) => {
                const message = form.dataset.confirmMessage || "Bạn có chắc muốn tiếp tục?";
                if (!window.confirm(message)) {
                    event.preventDefault();
                }
            });
        });
    };

    const initImageFallbacks = () => {
        document.querySelectorAll("img[data-fallback-src], img[data-fallback-toggle-target]").forEach((image) => {
            if (!(image instanceof HTMLImageElement) || image.dataset.fallbackBound === "true") {
                return;
            }

            image.dataset.fallbackBound = "true";
            image.addEventListener("error", () => {
                const fallbackSrc = image.dataset.fallbackSrc;
                if (fallbackSrc && image.dataset.fallbackApplied !== "src") {
                    image.dataset.fallbackApplied = "src";
                    image.src = fallbackSrc;
                    return;
                }

                const targetId = image.dataset.fallbackToggleTarget;
                if (!targetId) {
                    return;
                }

                image.classList.add("d-none");

                const target = document.getElementById(targetId);
                if (!(target instanceof HTMLElement)) {
                    return;
                }

                const removeClasses = (target.dataset.fallbackRemoveClass || "d-none")
                    .split(" ")
                    .map((value) => value.trim())
                    .filter(Boolean);
                const addClasses = (target.dataset.fallbackAddClass || "")
                    .split(" ")
                    .map((value) => value.trim())
                    .filter(Boolean);

                removeClasses.forEach((className) => target.classList.remove(className));
                addClasses.forEach((className) => target.classList.add(className));
            });
        });
    };

    const init = () => {
        initBootstrapValidation();
        initConfirmSubmissions();
        initImageFallbacks();
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
