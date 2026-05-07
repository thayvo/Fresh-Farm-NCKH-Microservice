const fs = require('fs');
const path = require('path');
const { chromium } = require('../floating-chat-verify/node_modules/playwright');

const outDir = __dirname;

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1600, height: 1400 }
  });
  const page = await context.newPage();

  await page.goto('https://localhost:7085/account/signin?returnUrl=%2Fsearch%3Fname%3Dkhoai', { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Identifier"]', 'abcdef123');
  await page.fill('input[name="Password"]', 'FreshFarm123!');
  await page.click('button[type="submit"]');
  await page.waitForURL(/https:\/\/localhost:7085\/search\?name=khoai/, { timeout: 30000 });
  await page.waitForTimeout(3000);

  const payload = await page.evaluate(async () => {
    const response = await fetch('/bff/product-search?name=khoai&page=1&pageSize=8', { credentials: 'include' });
    return {
      status: response.status,
      json: await response.json()
    };
  });

  await page.screenshot({
    path: path.join(outDir, 'search-seller-preference-verify.png'),
    fullPage: true
  });

  const result = {
    capturedAtUtc: new Date().toISOString(),
    status: payload.status,
    rankingAlgorithm: payload.json.rankingAlgorithm ?? null,
    rankingSignalSource: payload.json.rankingSignalSource ?? null,
    rankingFallbackReason: payload.json.rankingFallbackReason ?? null,
    signalBreakdown: payload.json.signalBreakdown ?? null,
    topItems: (payload.json.items ?? []).slice(0, 5).map(item => ({
      productId: item.productId,
      productName: item.productName,
      primarySellerId: item.primarySellerId,
      recommendationReason: item.recommendationReason
    }))
  };

  fs.writeFileSync(
    path.join(outDir, 'search-seller-preference-verify.json'),
    JSON.stringify(result, null, 2),
    'utf8');

  await browser.close();
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
