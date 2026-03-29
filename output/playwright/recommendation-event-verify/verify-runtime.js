const { chromium } = require("playwright");

(async () => {
    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await context.newPage();

    await page.goto("https://localhost:7085/", { waitUntil: "networkidle" });
    await page.waitForTimeout(1500);

    const suggestionLink = page.locator("a[data-action='suggestion-click']").first();
    if (await suggestionLink.count()) {
        await suggestionLink.click();
        await page.waitForLoadState("networkidle");
        await page.waitForTimeout(1500);
    }

    await page.goto("https://localhost:7085/products/104", { waitUntil: "networkidle" });
    await page.waitForTimeout(1500);

    const offerLink = page.locator("a[data-action='offer-recommendation-click']").first();
    if (await offerLink.count()) {
        await offerLink.click();
        await page.waitForTimeout(1200);
    }

    await page.goto("https://localhost:7085/search?q=rau", { waitUntil: "networkidle" });
    await page.waitForTimeout(1500);

    const relatedLink = page.locator("a[data-action='related-recommendation-click']").first();
    if (await relatedLink.count()) {
        await relatedLink.click();
        await page.waitForLoadState("networkidle");
        await page.waitForTimeout(1200);
    }

    await page.goto("https://localhost:7085/search?q=rau", { waitUntil: "networkidle" });
    await page.waitForTimeout(1500);

    const searchResultLink = page.locator("a[data-action='search-result-click']").first();
    if (await searchResultLink.count()) {
        await searchResultLink.click();
        await page.waitForLoadState("networkidle");
        await page.waitForTimeout(1200);
    }

    await browser.close();
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
