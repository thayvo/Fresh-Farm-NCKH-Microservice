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

  console.log('open sign-in');
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

  const sectionSummary = await page.evaluate(() => {
    const normalize = (value) => value ? value.replace(/\s+/g, ' ').trim() : null;
    const headings = Array.from(document.querySelectorAll('h1, h2, h3, h4'))
      .map((node) => normalize(node.textContent))
      .filter(Boolean);

    const bodyText = normalize(document.body.innerText) ?? '';
    return {
      title: document.title,
      url: location.href,
      headings,
      hasBuyAgain: bodyText.includes('Mua lại từ lịch sử'),
      hasBuyWithHistory: bodyText.includes('Mua kèm từ lịch sử'),
      hasReplenishSoon: bodyText.includes('Đến kỳ mua lại'),
      snippets: bodyText
        .split('\n')
        .map((line) => normalize(line))
        .filter(Boolean)
        .filter((line) =>
          line.includes('Mua lại từ lịch sử')
          || line.includes('Mua kèm từ lịch sử')
          || line.includes('Đến kỳ mua lại')
          || line.includes('Hay mua cùng')
          || line.includes('Sắp cần mua thêm')
          || line.includes('Sẵn để thêm vào giỏ')
          || line.includes('Sẵn để đặt lại ngay'))
        .slice(0, 30)
    };
  });

  const result = {
    capturedAtUtc: new Date().toISOString(),
    payload,
    sectionSummary
  };

  fs.writeFileSync(
    path.join(outDir, 'homepage-sections-longterm-verify.json'),
    JSON.stringify(result, null, 2),
    'utf8');

  await page.screenshot({
    path: path.join(outDir, 'homepage-sections-longterm-verify.png'),
    fullPage: true
  });

  await browser.close();
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
