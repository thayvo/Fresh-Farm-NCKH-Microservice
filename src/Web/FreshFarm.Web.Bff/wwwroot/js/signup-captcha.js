document.addEventListener("DOMContentLoaded", function () {
    var captchaImage = document.querySelector("[data-local-captcha-image]");
    var refreshButton = document.querySelector("[data-local-captcha-refresh]");

    if (!captchaImage || !refreshButton) {
        return;
    }

    refreshButton.addEventListener("click", function () {
        captchaImage.src = captchaImage.src.split("?")[0] + "?v=" + Date.now();
    });
});
