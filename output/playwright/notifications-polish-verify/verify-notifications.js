const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const outDir = __dirname;

async function captureState(page, name, url, bucket) {
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1200);

  bucket[name] = await page.evaluate(() => {
    const text = (selector) => document.querySelector(selector)?.textContent?.trim() ?? null;
    const normalize = (value) => value ? value.replace(/\s+/g, ' ').trim() : null;

    return {
      title: document.title,
      url: location.href,
      hero: text('.notification-hero h1'),
      stateSummary: normalize(document.querySelector('.bg-body-tertiary .text-muted.small')?.textContent),
      emptyTitle: text('.notification-empty-state .fw-semibold'),
      emptyDescription: normalize(document.querySelector('.notification-empty-state .text-muted')?.textContent),
      chips: Array.from(document.querySelectorAll('.bg-body-tertiary .badge'))
        .map((x) => normalize(x.textContent))
        .filter(Boolean),
      statCards: Array.from(document.querySelectorAll('.notification-stat-card'))
        .map((card) => normalize(card.textContent))
        .filter(Boolean),
      sectionTitles: Array.from(document.querySelectorAll('[data-notification-section] .fw-semibold'))
        .map((x) => normalize(x.textContent))
        .filter(Boolean)
        .slice(0, 8),
      actions: Array.from(document.querySelectorAll('a.btn, button.btn'))
        .map((x) => normalize(x.textContent))
        .filter(Boolean)
        .slice(0, 24)
    };
  });

  await page.screenshot({
    path: path.join(outDir, `${name}.png`),
    fullPage: true
  });
}

async function run() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 1200 }
  });
  const page = await context.newPage();
  const results = {};

  console.log('open sign-in');
  await page.goto('https://localhost:7085/account/signin?returnUrl=%2Faccount%2Fnotifications', { waitUntil: 'domcontentloaded' });
  await page.fill('input[name="Identifier"]', 'notifverify0323125000');
  await page.fill('input[name="Password"]', 'FreshFarm123!');
  console.log('submit sign-in');
  await page.click('button[type="submit"]');
  await page.waitForURL('**/account/notifications**', { timeout: 30000 });
  await page.waitForTimeout(1500);
  console.log('signed in', page.url());

  console.log('capture default');
  await captureState(page, 'notifications-default', 'https://localhost:7085/account/notifications', results);
  console.log('capture seller unread');
  await captureState(page, 'notifications-seller-unread', 'https://localhost:7085/account/notifications?type=seller_review_update&isRead=false&focusType=seller_review_update', results);
  console.log('capture order recent');
  await captureState(page, 'notifications-order-recent', 'https://localhost:7085/account/notifications?type=order_status&recentOnly=true&focusType=order_status', results);
  console.log('capture empty filtered');
  await captureState(page, 'notifications-empty-filtered', 'https://localhost:7085/account/notifications?q=khong-co-ket-qua-notification-verify', results);

  fs.writeFileSync(
    path.join(outDir, 'notifications-polish-verify.json'),
    JSON.stringify(results, null, 2),
    'utf8');
  console.log('wrote results');

  await browser.close();
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
