const fs = require("fs");
const path = require("path");
const { chromium } = require("playwright");

const outputDir = __dirname;
const screenshotPath = path.join(outputDir, "home-hybrid-runtime.png");
const jsonPath = path.join(outputDir, "home-hybrid-runtime.json");

(async () => {
    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await context.newPage();

    const activity = [];

    await page.goto("https://localhost:7085/search?q=rau", { waitUntil: "networkidle" });
    await page.waitForTimeout(1500);
    activity.push("search_opened");

    const searchResultLink = page.locator("a[data-action='search-result-click']").first();
    if (await searchResultLink.count()) {
        await searchResultLink.click();
        await page.waitForLoadState("networkidle");
        await page.waitForTimeout(1800);
        activity.push("search_result_clicked");
    }

    await page.goto("https://localhost:7085/products/104", { waitUntil: "networkidle" });
    await page.waitForTimeout(1800);
    activity.push("product_104_viewed");

    await page.goto("https://localhost:7085/", { waitUntil: "networkidle" });
    await page.waitForTimeout(2000);
    activity.push("home_revisited");

    const recommendationPayload = await page.evaluate(async () => {
        const response = await fetch("/bff/recommendations/home?limit=4", {
            credentials: "same-origin",
            headers: {
                "Accept": "application/json"
            }
        });

        const text = await response.text();
        let body = null;
        try {
            body = JSON.parse(text);
        } catch {
            body = { raw: text };
        }

        return {
            status: response.status,
            body
        };
    });

    await page.screenshot({ path: screenshotPath, fullPage: true });

    const result = {
        createdAtUtc: new Date().toISOString(),
        activity,
        recommendationStatus: recommendationPayload.status,
        algorithm: recommendationPayload.body?.algorithm ?? null,
        placement: recommendationPayload.body?.placement ?? null,
        topItems: Array.isArray(recommendationPayload.body?.items)
            ? recommendationPayload.body.items.slice(0, 4).map(item => ({
                productId: item.productId,
                productName: item.productName,
                recommendationReason: item.recommendationReason,
                recommendationScore: item.recommendationScore
            }))
            : [],
        screenshotPath
    };

    fs.writeFileSync(jsonPath, JSON.stringify(result, null, 2), "utf8");
    await browser.close();
})().catch((error) => {
    console.error(error);
    process.exit(1);
});
