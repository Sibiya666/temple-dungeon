const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { chromium } = require('C:/Users/sibiya666/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

// Verify the portable file exactly as delivered, using a fresh headless browser profile.
async function main() {
  const out = path.join(__dirname, 'qa');
  fs.mkdirSync(out, { recursive: true });
  console.log('QA: launching browser');
  const browser = await chromium.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: true,
    timeout: 25000,
  });
  console.log('QA: browser launched');
  const report = { errors: [], externalRequests: [], checks: [] };
  const check = (condition, message) => {
    report.checks.push({ message, passed: !!condition });
    if (!condition) throw new Error(message);
  };
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, reducedMotion: 'reduce' });
    const page = await context.newPage();
    page.on('pageerror', error => report.errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') report.errors.push(message.text()); });
    await page.route(/^https?:/, route => { report.externalRequests.push(route.request().url()); return route.abort(); });
    await page.goto(pathToFileURL(path.join(__dirname, 'index.html')).href);
    console.log('QA: file loaded');
    await page.evaluate(async () => {
      for (const image of document.images) image.loading = 'eager';
      await document.fonts.ready;
      await Promise.all([...document.images].filter(image => image.getAttribute('src')).map(image => image.decode().catch(() => {})));
    });
    const inspect = () => page.evaluate(() => ({
      brokenImages: [...document.images].filter(i => i.getAttribute('src') && (!i.complete || !i.naturalWidth)).map(i => i.alt),
      overflow: document.documentElement.scrollWidth > innerWidth + 1,
      imageCount: document.images.length,
      embedded: [...document.images].filter(i => i.getAttribute('src')).every(i => i.getAttribute('src').startsWith('data:image/')),
      sections: [...document.querySelectorAll('main section[id]')].map(s => s.id),
    }));
    report.desktop = await inspect();
    console.log('QA: images decoded');
    check(report.desktop.brokenImages.length === 0, 'All desktop images load');
    check(report.desktop.imageCount >= 6 && report.desktop.embedded, 'Images are embedded and portable');
    check(!report.desktop.overflow, 'No desktop horizontal overflow');
    await page.screenshot({ path: path.join(out, 'desktop-top.png') });
    await page.screenshot({ path: path.join(out, 'desktop-full.png'), fullPage: true });
    for (const id of ['icons', 'skills', 'tower']) {
      await page.locator('#' + id).evaluate(section => section.scrollIntoView({ block: 'start', behavior: 'instant' }));
      await page.evaluate(() => document.activeElement?.blur());
      await page.screenshot({ path: path.join(out, 'desktop-' + id + '.png') });
    }
    const specimen = page.locator('button.node').first();
    await specimen.click();
    check(await specimen.getAttribute('aria-pressed') === 'true', 'Component selection sample responds');
    await specimen.click();
    check(await specimen.getAttribute('aria-pressed') === 'false', 'Component selection sample resets');
    const navigation = page.locator('nav a[href^="#"]');
    check(await navigation.count() >= 4, 'Section navigation is available');
    const link = navigation.nth(Math.min(2, (await navigation.count()) - 1));
    const target = await link.getAttribute('href');
    await link.click();
    check(await page.evaluate(hash => location.hash === hash, target), 'Navigation opens its named section');
    const zoom = page.locator('button[data-zoom]').first();
    check(await zoom.count() === 1, 'Image zoom trigger is available');
    await zoom.scrollIntoViewIfNeeded();
    await zoom.click();
    check(await page.locator('dialog[open]').count() === 1, 'Image dialog opens');
    await page.keyboard.press('Escape');
    check(await page.locator('dialog[open]').count() === 0, 'Escape closes image dialog');
    check(await zoom.evaluate(el => document.activeElement === el), 'Dialog restores trigger focus');
    await page.evaluate(() => document.activeElement?.blur());
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    const beforeKey = await page.locator('nav a[aria-current]').getAttribute('href');
    await page.keyboard.press('ArrowRight');
    await page.waitForFunction(previous => document.querySelector('nav a[aria-current]')?.getAttribute('href') !== previous, beforeKey);
    const afterKey = await page.locator('nav a[aria-current]').getAttribute('href');
    check(afterKey !== beforeKey, 'Arrow key advances the presentation');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(pathToFileURL(path.join(__dirname, 'index.html')).href);
    await page.evaluate(async () => {
      for (const image of document.images) image.loading = 'eager';
      await document.fonts.ready;
      await Promise.all([...document.images].filter(image => image.getAttribute('src')).map(image => image.decode().catch(() => {})));
    });
    report.mobile = await inspect();
    check(!report.mobile.overflow, 'No mobile horizontal overflow');
    check(report.mobile.brokenImages.length === 0, 'All mobile images load');
    await page.screenshot({ path: path.join(out, 'mobile-top.png') });
    await page.screenshot({ path: path.join(out, 'mobile-full.png'), fullPage: true });
    await page.emulateMedia({ media: 'print' });
    check(await page.evaluate(() => !document.querySelector('nav').getClientRects().length), 'Print hides presentation navigation');
    check(report.errors.length === 0, 'No browser console or page errors');
    check(report.externalRequests.length === 0, 'No external network dependencies');
    fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2));
    console.log(JSON.stringify(report, null, 2));
  } finally {
    fs.writeFileSync(path.join(out, 'report.json'), JSON.stringify(report, null, 2));
    await browser.close();
  }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
