const { chromium } = require("../floating-chat-verify/node_modules/playwright");

async function main() {
  const browser = await chromium.launch({
    headless: false,
    slowMo: 250,
  });

  const context = await browser.newContext({
    ignoreHTTPSErrors: true,
    viewport: { width: 1440, height: 960 },
  });

  const pages = [
    "https://localhost:7085/",
    "https://localhost:7085/products?q=rau",
    "https://localhost:7085/products/104",
    "https://localhost:7085/shops",
    "https://localhost:7085/shop/47",
    "https://localhost:7085/account/signin",
    "https://localhost:7085/account/signup",
  ];

  for (let index = 0; index < pages.length; index += 1) {
    const page = index === 0 ? await context.newPage() : await context.newPage();
    await page.goto(pages[index], { waitUntil: "domcontentloaded", timeout: 30000 });
  }

  const [firstPage] = context.pages();
  if (firstPage) {
    await firstPage.bringToFront();
  }

  // Keep the headed browser open so the user can inspect it directly.
  await new Promise(() => {});
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
