/*
 * Captures the screenshots the PUBLIC guide ships with, against the local dev stack — a server with
 * neutral content, never a real one. The guide bundled with the plugin must not show anybody's library.
 *
 * Run it after deploying the plugin to the stack (see deploy-plugin.sh) and signing in once:
 *
 *   npm --prefix ../tests/e2e ci                  # once, for playwright
 *   JC_BASE_URL=http://127.0.0.1:8097 JC_USER=Testor JC_PASSWORD='Test101!' \
 *     node capture-guide-shots.mjs
 *
 * Point it at 8097 (Jellyfin 10.11), not 8096: header.js anchors its UI to the 10.11 web DOM, so on the
 * 12.0 RC instance there is no header to shoot. Sign in as an ORDINARY user — an admin's header carries
 * the Admin tab and the popup a "request on behalf of" picker, neither of which a reader ever sees.
 *
 * It writes the files into Jellyfin.Plugin.JellyCrowd/Web/img/ and prints the "images" block to paste
 * into Web/guide-content.json, together with the figs each step should declare.
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const BASE = process.env.JC_BASE_URL || 'http://127.0.0.1:8096';
const USER = process.env.JC_USER || 'Testor';
const PASSWORD = process.env.JC_PASSWORD || 'Test101!';
const OUT = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'Jellyfin.Plugin.JellyCrowd', 'Web', 'img');

// Each shot: the file it lands in, the viewport it is taken at, and how to get to it. Keep the sizes —
// the guide's layout is built around them (the notification panel is the narrow one).
const SHOTS = [
  { name: 'guide-catalog.jpg', width: 1600, height: 1000, go: async page => openTab(page, 'Catalog') },
  {
    name: 'guide-search.jpg', width: 1600, height: 1000, go: async page => {
      await openTab(page, 'Catalog');
      // A term whose TMDB results stay presentable — this figure ships in the public guide. It is also
      // in the dev stack's library, so the green "Available" badge the guide talks about shows up.
      await page.fill('#jcSearchInput', 'dune');
      await page.press('#jcSearchInput', 'Enter');
      await page.locator('.jellycrowd-card').first().waitFor();
    }
  },
  {
    name: 'guide-detail.jpg', width: 1600, height: 1000, go: async page => {
      await openTab(page, 'Catalog');
      // Step 3 of the guide splits in two, and so do these two figures. This one is the "already on the
      // server" case: search a title the library has, so the popup shows Open in Jellyfin / Add to my
      // library. Picking the first card off the landing grid gave the same shot as guide-request.
      await page.fill('#jcSearchInput', 'dune');
      await page.press('#jcSearchInput', 'Enter');
      await page.locator('.jellycrowd-card:has(.jellycrowd-badge)').first().click();
      await page.locator('.jellycrowd-modal[role="dialog"]').waitFor();
    }
  },
  {
    name: 'guide-request.jpg', width: 1600, height: 1000, go: async page => {
      await openTab(page, 'Catalog');
      // A title that is NOT in the library yet: its popup shows the Request button and the date picker.
      await page.locator('.jellycrowd-card:not(:has(.jellycrowd-badge))').first().click();
      await page.locator('.jellycrowd-modal[role="dialog"]').waitFor();
    }
  },
  { name: 'guide-requests.jpg', width: 1600, height: 1000, go: async page => openTab(page, 'My requests') },
  {
    name: 'guide-notif.jpg', width: 722, height: 946, go: async page => {
      await page.click('.jcHeaderBell');
      // The panel paints a "Loading…" placeholder first, so wait for the rendered list (its header
      // carries the ⚙ settings button) rather than for the panel element itself.
      await page.locator('.jcBellPanel button[aria-label]').first().waitFor();
    }
  }
];

// Posters, cast photos and the related-media row stream in after the view is up; screenshotting
// before they land leaves grey placeholders in the figure.
async function settleImages(page) {
  await page.waitForFunction(
    () => Array.from(document.images).every(img => !img.getAttribute('src') || img.complete),
    null, { timeout: 15000 }
  ).catch(() => { /* a stalled image must not cost us the shot */ });
  await page.waitForTimeout(600);
}

async function openTab(page, label) {
  const tab = page.locator(':is(a, button.jcHeaderTab):visible', { hasText: label }).first();
  await tab.click();
  await page.locator('.jellycrowd-view:visible').first().waitFor();
  await page.waitForTimeout(1500); // posters finish loading; a half-drawn grid makes a poor screenshot
}

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
const page = await context.newPage();

await page.goto(`${BASE}/web/index.html`);
await page.locator('#txtManualName').waitFor({ state: 'visible' });
await page.fill('#txtManualName', USER);
await page.fill('#txtManualPassword', PASSWORD);
await page.locator('.button-submit').first().click();
await page.locator('#txtManualName').waitFor({ state: 'hidden', timeout: 30000 });

fs.mkdirSync(OUT, { recursive: true });
for (const shot of SHOTS) {
  await page.setViewportSize({ width: shot.width, height: shot.height });
  await page.goto(`${BASE}/web/index.html#/home.html`);
  await page.reload();                       // same-document hash nav: force a real load so no modal survives
  await page.locator('.jcHeaderTab, .jcHeaderNav').first().waitFor({ timeout: 30000 });
  await page.waitForTimeout(800);
  try {
    await shot.go(page);
    await settleImages(page);
    await page.screenshot({ path: path.join(OUT, shot.name), type: 'jpeg', quality: 82 });
    console.log('✓', shot.name);
  } catch (error) {
    console.error('✗', shot.name, '—', error.message.split('\n')[0]);
  }
}
await browser.close();

console.log('\nPaste into Web/guide-content.json:');
console.log(JSON.stringify({
  images: {
    catalog: 'guide-catalog.jpg', search: 'guide-search.jpg', detail: 'guide-detail.jpg',
    request: 'guide-request.jpg', requests: 'guide-requests.jpg', notif: 'guide-notif.jpg'
  }
}, null, 2));
console.log('\n…and give each step its figs, e.g. steps[0].figs = [["catalog", "<caption>"]].');
