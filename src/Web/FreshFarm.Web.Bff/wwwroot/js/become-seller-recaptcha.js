document.addEventListener("DOMContentLoaded", function () {
    var form = document.getElementById("becomeSellerForm");
    var tokenInput = document.getElementById("Form_RecaptchaToken");

    if (!form || !tokenInput) {
        return;
    }

    var siteKey = form.getAttribute("data-recaptcha-site-key") || "";
    var action = form.getAttribute("data-recaptcha-action") || "become_seller";
    var isSubmittingWithToken = false;

    form.addEventListener("submit", function (event) {
        if (isSubmittingWithToken) {
            isSubmittingWithToken = false;
            return;
        }

        event.preventDefault();
        tokenInput.value = "";

        if (!siteKey || !window.grecaptcha || typeof window.grecaptcha.execute !== "function") {
            isSubmittingWithToken = true;
            form.requestSubmit();
            return;
        }

        window.grecaptcha.ready(function () {
            window.grecaptcha.execute(siteKey, { action: action })
                .then(function (token) {
                    tokenInput.value = typeof token === "string" ? token : "";
                    isSubmittingWithToken = true;
                    form.requestSubmit();
                })
                .catch(function () {
                    tokenInput.value = "";
                    isSubmittingWithToken = true;
                    form.requestSubmit();
                });
        });
    });
});
