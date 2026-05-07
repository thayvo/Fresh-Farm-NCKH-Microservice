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

  await page.goto('https://localhost:7085/account/signin?returnUrl=%2Fproducts%2F1', { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Identifier"]', 'abcdef123');
  await page.fill('input[name="Password"]', 'FreshFarm123!');
  await page.click('button[type="submit"]');
  await page.waitForURL('https://localhost:7085/products/1', { timeout: 30000 });
  await page.waitForTimeout(3000);

  const payload = await page.evaluate(async () => {
    const response = await fetch('/bff/recommendations/products/1/similar?limit=4', { credentials: 'include' });
    return {
      status: response.status,
      json: await response.json()
    };
  });

  await page.screenshot({
    path: path.join(outDir, 'similar-longterm-preference-verify.png'),
    fullPage: true
  });

  const result = {
    capturedAtUtc: new Date().toISOString(),
    status: payload.status,
    algorithm: payload.json.algorithm ?? null,
    signalSource: payload.json.signalSource ?? null,
    preferenceSignalSource: payload.json.preferenceSignalSource ?? null,
    collaborativeSignalSource: payload.json.collaborativeSignalSource ?? null,
    fallbackReason: payload.json.fallbackReason ?? null,
    preferenceFallbackReason: payload.json.preferenceFallbackReason ?? null,
    collaborativeFallbackReason: payload.json.collaborativeFallbackReason ?? null,
    topItems: (payload.json.items ?? []).slice(0, 4).map((item) => ({
      productId: item.productId,
      productName: item.productName,
      primarySellerId: item.primarySellerId,
      origin: item.origin,
      recommendationReason: item.recommendationReason,
      recommendationTags: item.recommendationTags ?? []
    }))
  };

  fs.writeFileSync(
    path.join(outDir, 'similar-longterm-preference-verify.json'),
    JSON.stringify(result, null, 2),
    'utf8');

  await browser.close();
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
