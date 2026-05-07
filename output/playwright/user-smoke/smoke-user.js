const fs = require("fs");
const path = require("path");
const { chromium } = require("../floating-chat-verify/node_modules/playwright");

const baseUrl = "https://localhost:7085";
const artifactDir = __dirname;

function slugify(value) {
  return String(value)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80);
}

async function capturePage(page, route, expectTitle) {
  const consoleMessages = [];
  const pageErrors = [];
  const failedRequests = [];

  const onConsole = msg => {
    if (msg.type() === "error" || msg.type() === "warning") {
      consoleMessages.push(`${msg.type()}: ${msg.text()}`);
    }
  };
  const onPageError = err => pageErrors.push(err.message);
  const onRequestFailed = req => failedRequests.push(`${req.method()} ${req.url()} :: ${req.failure()?.errorText ?? "unknown"}`);

  page.on("console", onConsole);
  page.on("pageerror", onPageError);
  page.on("requestfailed", onRequestFailed);

  const response = await page.goto(`${baseUrl}${route}`, { waitUntil: "networkidle", timeout: 30000 });
  await page.setViewportSize({ width: 1440, height: 1200 });
  await page.screenshot({ path: path.join(artifactDir, `${slugify(route || "home")}.png`), fullPage: true });

  const title = await page.title();
  const bodyText = await page.locator("body").innerText();

  page.off("console", onConsole);
  page.off("pageerror", onPageError);
  page.off("requestfailed", onRequestFailed);

  return {
    route,
    status: response?.status() ?? null,
    ok: response?.ok() ?? false,
    title,
    expectTitle,
    titleMatched: expectTitle ? title.toLowerCase().includes(expectTitle.toLowerCase()) : true,
    bodySample: bodyText.replace(/\s+/g, " ").trim().slice(0, 280),
    consoleMessages,
    pageErrors,
    failedRequests,
  };
}

async function main() {
  fs.mkdirSync(artifactDir, { recursive: true });

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  const routes = [
    { route: "/", expectTitle: "FreshFarm" },
    { route: "/products?q=rau", expectTitle: "FreshFarm" },
    { route: "/products/104", expectTitle: "FreshFarm" },
    { route: "/shops", expectTitle: "FreshFarm" },
    { route: "/shop/47", expectTitle: "FreshFarm" },
    { route: "/account/signin", expectTitle: "Đăng nhập" },
    { route: "/account/signup", expectTitle: "Đăng ký" },
    { route: "/cart", expectTitle: "Đăng nhập" },
  ];

  const results = [];
  for (const item of routes) {
    results.push(await capturePage(page, item.route, item.expectTitle));
  }

  const summary = {
    baseUrl,
    generatedAt: new Date().toISOString(),
    routes: results,
    totals: {
      total: results.length,
      httpOk: results.filter(x => x.ok).length,
      titleMatched: results.filter(x => x.titleMatched).length,
      routesWithClientErrors: results.filter(x => x.consoleMessages.length || x.pageErrors.length || x.failedRequests.length).length,
    },
  };

  fs.writeFileSync(path.join(artifactDir, "smoke-user-results.json"), JSON.stringify(summary, null, 2), "utf8");

  await browser.close();
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
