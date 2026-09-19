'use strict';

// Opening the overlay, driven through the REAL header.js — no Jellyfin needed. The page below is a stub
// shell carrying only what header.js looks for (.skinHeader / .headerRight / .headerHomeButton, an
// ApiClient, the endpoints it calls at boot), which is enough to reproduce the two ways opening a panel
// has already broken:
//
//   1. The overlay took its geometry from jellycrowd.css, which each VIEW was responsible for loading.
//      Opening a view that did not load it, as the first view of the session, left the overlay unstyled:
//      a static div as tall as its content instead of a fixed panel. Opening another panel first hid it.
//   2. Opening a panel sends the page behind it Home; that navigation is ours and must not close the
//      overlay. A flag cleared on a 200 ms timer lost the race whenever the client reported the
//      navigation later than that.
//
// Both are invisible to a unit test and to a mock of the panel, and both shipped. Hence this.

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { test, expect } = require('@playwright/test');

const WEB = path.join(__dirname, '..', '..', 'Jellyfin.Plugin.JellyCrowd', 'Web');
const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.jpg': 'image/jpeg', '.png': 'image/png', '.json': 'application/json'
};
const ENDPOINTS = {
  '/JellyCrowd/Settings/Language': { Language: 'fr', Hidden: false, CommentsEnabled: true, GuideEnabled: true, SkipOutroEnabled: false, HideNativeDrawer: false },
  '/JellyCrowd/Settings/Visibility': { Hidden: false, IsChild: false },
  '/JellyCrowd/Settings/Branding': { Enabled: false },
  '/JellyCrowd/Settings/LocalIntros': { Enabled: false },
  '/JellyCrowd/Quota/Me': { UsedBytes: 3221225472, QuotaBytes: 32212254720, ReservedBytes: 0, Unlimited: false },
  '/JellyCrowd/Notifications/Mine': { Unread: 0, Items: [] },
  '/JellyCrowd/Requests/Mine': []
};

const SHELL = `<!doctype html><html><head><meta charset="utf-8"><style>
  body { margin:0; background:#101013; color:#fff; font-family:sans-serif; min-height:300vh; }
  .skinHeader { position:fixed; top:0; left:0; right:0; height:56px; background:#202020; z-index:1000;
                display:flex; align-items:center; justify-content:space-between; padding:0 12px; }
  .headerRight { display:flex; align-items:center; gap:.4em; }
</style></head><body>
<div class="skinHeader">
  <button class="headerHomeButton">home</button><div class="headerTabs"></div>
  <div class="headerRight"><button class="headerUserButton" title="Testor">me</button></div>
</div>
<div style="padding:80px 24px"><h1>background page</h1></div>
<script>
  window.ApiClient = {
    getUrl: p => '/' + String(p).replace(/^\\//, ''),
    serverId: () => 'srv', getCurrentUserId: () => 'user-1', accessToken: () => 'tok',
    getCurrentUser: () => Promise.resolve({ Name: 'Testor', PrimaryImageTag: null }),
    getItem: () => Promise.resolve({ Type: 'Movie', Name: 'x', ProviderIds: {} }),
    ajax: o => fetch(o.url, { method: o.type || 'GET', headers: o.contentType ? { 'Content-Type': o.contentType } : {}, body: o.data })
      .then(r => (o.dataType === 'json' ? r.json() : r))
  };
  // Jellyfin's Home navigation, as far as the overlay is concerned.
  document.querySelector('.headerHomeButton').addEventListener('click', () => { location.hash = '#/home.html'; });
</script>
<script src="/JellyCrowd/Web/header.js"></script></body></html>`;

let server;
let origin;

test.beforeAll(async () => {
  server = http.createServer((req, res) => {
    const url = decodeURIComponent(req.url.split('?')[0]);
    const send = (body, type) => { res.writeHead(200, { 'Content-Type': type }); res.end(body); };
    // Jellyfin serves its client from /web/, so anything the view resolves relatively resolves
    // against THAT, not against the plugin's asset route. Serving the stub anywhere else would
    // quietly make relative paths work here and fail in production.
    if (url === '/web/index.html') { return send(SHELL, 'text/html; charset=utf-8'); }
    if (ENDPOINTS[url]) { return send(JSON.stringify(ENDPOINTS[url]), 'application/json'); }
    const file = path.join(WEB, url.replace(/^\/JellyCrowd\/Web/, ''));
    if (!file.startsWith(WEB)) { res.writeHead(403); return res.end('no'); }
    return fs.readFile(file, (err, buf) => err
      ? (res.writeHead(404), res.end('{}'))
      : send(buf, TYPES[path.extname(file)] || 'application/octet-stream'));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  origin = `http://127.0.0.1:${server.address().port}`;
});

test.afterAll(async () => { await new Promise(resolve => server.close(resolve)); });

// Away from Home is what makes opening a panel navigate the page behind it.
const openGuideFromADeepPage = async page => {
  await page.goto(`${origin}/web/index.html#/details?id=abc`);
  await page.locator('.jcHeaderLink-guide').waitFor();
  await page.click('.jcHeaderLink-guide');
};

test('opening a panel as the first view of the session lays it out properly', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 900 });
  await openGuideFromADeepPage(page);
  await page.locator('.jcGuide h1').waitFor();

  const panel = await page.evaluate(() => {
    const el = document.querySelector('.jellycrowd-overlay');
    const box = el.getBoundingClientRect();
    return {
      position: getComputedStyle(el).position,
      display: getComputedStyle(el).display,
      width: Math.round(box.width), height: Math.round(box.height), top: Math.round(box.top),
      viewport: { w: window.innerWidth, h: window.innerHeight }
    };
  });

  expect(panel.position).toBe('fixed');
  expect(panel.display).toBe('flex');
  expect(panel.width).toBe(panel.viewport.w);
  // Anchored below the native header, and never taller than the screen (it was as tall as its content).
  expect(panel.top).toBeGreaterThan(0);
  expect(panel.height).toBeLessThanOrEqual(panel.viewport.h);
});

test('the panel survives the navigation it triggers itself, however late it lands', async ({ page }) => {
  await openGuideFromADeepPage(page);
  await page.locator('.jcGuide h1').waitFor();

  // A client that reports its navigation well after the old 200 ms window.
  await page.waitForTimeout(900);
  await page.evaluate(() => window.dispatchEvent(new HashChangeEvent('hashchange')));
  await page.waitForTimeout(200);
  await expect(page.locator('.jellycrowd-overlay')).toBeVisible();

  // A real navigation, once the opening burst is over, still closes it.
  await page.waitForTimeout(2600);
  await page.evaluate(() => { location.hash = '#/details?id=zzz'; });
  await page.waitForTimeout(300);
  await expect(page.locator('.jellycrowd-overlay')).toBeHidden();
});
