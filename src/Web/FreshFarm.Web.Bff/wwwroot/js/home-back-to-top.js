(function () {
    const button = document.querySelector("[data-back-to-top]");
    if (!button) {
        return;
    }

    const toggleButton = () => {
        button.classList.toggle("is-visible", window.scrollY > 420);
    };

    button.addEventListener("click", () => {
        window.scrollTo({ top: 0, behavior: "smooth" });
    });

    window.addEventListener("scroll", toggleButton, { passive: true });
    toggleButton();
})();
