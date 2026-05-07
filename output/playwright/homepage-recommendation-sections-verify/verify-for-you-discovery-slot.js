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

  const payload = await page.evaluate(async () => {
    const response = await fetch('/bff/recommendations/home?limit=12', { credentials: 'include' });
    const json = await response.json();
    return {
      status: response.status,
      json
    };
  });

  const forYouSection = payload.json.sections.find(section => section.id === 'for_you') ?? null;
  const discoveryItems = (forYouSection?.items ?? []).filter(item => {
    const reason = item.recommendationReason ?? '';
    return (reason.includes('Hợp nhóm') || reason.includes('Đúng vùng'))
      && !reason.startsWith('Bạn ');
  });

  const domSummary = await page.evaluate(() => {
    const normalize = (value) => value ? value.replace(/\s+/g, ' ').trim() : null;
    const cards = Array.from(document.querySelectorAll('section, div, article'))
      .map(node => normalize(node.textContent))
      .filter(Boolean)
      .filter(text => text.includes('Dành cho bạn'))
      .slice(0, 3);

    return {
      title: document.title,
      url: location.href,
      cards
    };
  });

  const result = {
    capturedAtUtc: new Date().toISOString(),
    payloadStatus: payload.status,
    forYouSectionId: forYouSection?.id ?? null,
    forYouTitles: (forYouSection?.items ?? []).map(item => ({
      productId: item.productId,
      productName: item.productName,
      reason: item.recommendationReason
    })),
    discoveryItems,
    domSummary
  };

  fs.writeFileSync(
    path.join(outDir, 'for-you-discovery-slot-verify.json'),
    JSON.stringify(result, null, 2),
    'utf8');

  await page.screenshot({
    path: path.join(outDir, 'for-you-discovery-slot-verify.png'),
    fullPage: true
  });

  await browser.close();
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
