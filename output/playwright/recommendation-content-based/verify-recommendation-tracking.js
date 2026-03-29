const { chromium } = require("playwright");

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 1100 }
  });

  const homePage = await context.newPage();
  await homePage.goto("https://localhost:7085/", { waitUntil: "networkidle" });
  await homePage.waitForTimeout(4000);
  await homePage.screenshot({ path: "output/playwright/recommendation-content-based/home-tracking-verify.png", fullPage: true });

  const productPage = await context.newPage();
  await productPage.goto("https://localhost:7085/products/104", { waitUntil: "networkidle" });
  await productPage.waitForTimeout(5000);
  await productPage.screenshot({ path: "output/playwright/recommendation-content-based/product-104-tracking-before-click.png", fullPage: true });

  const similarLink = productPage.locator("a[data-action='similar-recommendation-click']").first();
  await similarLink.waitFor({ state: "visible", timeout: 15000 });
  const href = await similarLink.getAttribute("href");
  await similarLink.click();
  await productPage.waitForTimeout(2500);
  await productPage.screenshot({ path: "output/playwright/recommendation-content-based/product-104-tracking-after-click.png", fullPage: true });

  await browser.close();

  console.log(JSON.stringify({
    ok: true,
    clickedHref: href,
    capturedAtUtc: new Date().toISOString()
  }));
}

run().catch((error) => {
  console.error(JSON.stringify({
    ok: false,
    error: String(error && error.stack ? error.stack : error)
  }));
  process.exit(1);
});
