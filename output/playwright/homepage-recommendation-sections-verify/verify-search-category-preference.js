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

  await page.goto('https://localhost:7085/account/signin?returnUrl=%2F', { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Identifier"]', 'abcdef123');
  await page.fill('input[name="Password"]', 'FreshFarm123!');
  await page.click('button[type="submit"]');
  await page.waitForURL('https://localhost:7085/', { timeout: 30000 });
  await page.waitForTimeout(2500);

  const homePayload = await page.evaluate(async () => {
    const response = await fetch('/bff/recommendations/home?limit=12', { credentials: 'include' });
    return {
      status: response.status,
      json: await response.json()
    };
  });

  const searchPayload = await page.evaluate(async () => {
    const response = await fetch('/bff/product-search?name=rau&page=1&pageSize=8', { credentials: 'include' });
    return {
      status: response.status,
      json: await response.json()
    };
  });

  await page.goto('https://localhost:7085/search?name=rau', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(3000);
  await page.screenshot({
    path: path.join(outDir, 'search-category-preference-verify.png'),
    fullPage: true
  });

  const forYouSection = homePayload.json.sections.find(section => section.id === 'for_you') ?? null;
  const result = {
    capturedAtUtc: new Date().toISOString(),
    homeStatus: homePayload.status,
    searchStatus: searchPayload.status,
    forYouTopItems: (forYouSection?.items ?? []).slice(0, 4).map(item => ({
      productId: item.productId,
      productName: item.productName,
      recommendationReason: item.recommendationReason
    })),
    searchRankingAlgorithm: searchPayload.json.rankingAlgorithm ?? null,
    searchRankingSignalSource: searchPayload.json.rankingSignalSource ?? null,
    searchFallbackReason: searchPayload.json.rankingFallbackReason ?? null,
    searchTopItems: (searchPayload.json.items ?? []).slice(0, 5).map(item => ({
      productId: item.productId,
      productName: item.productName,
      categoryId: item.categoryId,
      categoryName: item.categoryName,
      recommendationReason: item.recommendationReason
    }))
  };

  fs.writeFileSync(
    path.join(outDir, 'search-category-preference-verify.json'),
    JSON.stringify(result, null, 2),
    'utf8');

  await browser.close();
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
