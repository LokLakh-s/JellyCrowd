'use strict';

// Guide layout, checked in a real browser WITHOUT a Jellyfin instance: the view is mounted in a faithful
// copy of the overlay (the plugin's own stylesheet, the DOM ensureOverlay builds, the close button that
// floats over the top-right corner) and served under the same Content-Security-Policy the production
// server sends. That CSP is the point: it forbids data: images, which is how six screenshots silently
// stopped rendering, and the close button is what the language switch used to collide with on narrow
// windows. Neither failure needs a live server to reproduce, so this runs standalone.

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { test, expect } = require('@playwright/test');

const WEB = path.join(__dirname, '..', '..', 'Jellyfin.Plugin.JellyCrowd', 'Web');
const CSP = "default-src https: data: blob: ; img-src 'self' https://* ; style-src 'self' 'unsafe-inline';"
  + " script-src 'self' 'unsafe-inline'; connect-src 'self'; object-src 'none'; font-src 'self'";
const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.jpg': 'image/jpeg', '.png': 'image/png', '.json': 'application/json'
};

// A page that mounts the guide exactly as showView() does, inside the overlay ensureOverlay() builds.
// The deliberately clashing h1/h2/ul rules stand in for a client whose styles could bleed into the view
// now that it is no longer isolated in an iframe.
const HOST_PAGE = `<!doctype html><html><head><meta charset="utf-8">
<link rel="stylesheet" href="/JellyCrowd/Web/jellycrowd.css">
<style>
  body { margin:0; background:#101013; color:#fff; font-family:"Noto Sans",sans-serif; }
  h1, h2 { font-family:serif; color:#00a4dc; }
  ul { list-style:square; padding-left:3em; }
</style></head><body>
<h2 id="hostHeading">a heading belonging to the client, not to the guide</h2>
<script>
  const overlay = document.createElement('div');
  overlay.className = 'jellycrowd-overlay';
  overlay.style.top = '56px';
  const viewHost = document.createElement('div');
  viewHost.className = 'jellycrowd-overlay-views';
  overlay.appendChild(viewHost);
  const close = document.createElement('button');
  close.id = 'jcClose';
  close.textContent = '\\u00d7';
  close.style.cssText = 'position:absolute;top:.5em;right:.7em;z-index:5;width:2.1em;height:2.1em;border:0;border-radius:50%;background:rgba(0,0,0,.55);color:#fff;font-size:1.4em;line-height:1;cursor:pointer;display:flex;align-items:center;justify-content:center;';
  overlay.appendChild(close);
  document.body.appendChild(overlay);
  fetch('/JellyCrowd/Web/guide.html').then(r => r.text()).then(html => {
    const container = document.createElement('div');
    container.className = 'jellycrowd-view';
    viewHost.appendChild(container);
    container.innerHTML = html;
    container.querySelectorAll('script').forEach(old => {
      const fresh = document.createElement('script');
      for (const a of old.attributes) { fresh.setAttribute(a.name, a.value); }
      if (!old.src) { fresh.textContent = old.textContent; }
      old.parentNode.replaceChild(fresh, old);
    });
  });
</script></body></html>`;

let server;
let origin;

test.beforeAll(async () => {
  server = http.createServer((req, res) => {
    const url = decodeURIComponent(req.url.split('?')[0]);
    const send = (body, type) => {
      res.writeHead(200, { 'Content-Type': type, 'Content-Security-Policy': CSP, 'X-Content-Type-Options': 'nosniff' });
      res.end(body);
    };
    // Jellyfin serves its client from /web/, so anything the view resolves relatively resolves
    // against THAT, not against the plugin's asset route. Serving the stub anywhere else would
    // quietly make relative paths work here and fail in production.
    if (url === '/web/index.html') { return send(HOST_PAGE, 'text/html; charset=utf-8'); }
    const file = path.join(WEB, url.replace(/^\/JellyCrowd\/Web/, ''));
    if (!file.startsWith(WEB)) { res.writeHead(403); return res.end('no'); }
    return fs.readFile(file, (err, buf) => err
      ? (res.writeHead(404), res.end('404'))
      : send(buf, TYPES[path.extname(file)] || 'application/octet-stream'));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  origin = `http://127.0.0.1:${server.address().port}`;
});

test.afterAll(async () => { await new Promise(resolve => server.close(resolve)); });

test('every screenshot renders under the production CSP', async ({ page }) => {
  await page.goto(`${origin}/web/index.html`);
  await page.locator('.jcGuide .faq-sec').waitFor();

  const images = await page.$$eval('.jcGuide img', els =>
    els.map(e => ({ src: e.getAttribute('src'), w: e.naturalWidth })));
  expect(images.length).toBeGreaterThanOrEqual(6);
  for (const image of images) {
    // A data: URI is refused by this CSP, and a decoded image always has a width.
    expect(image.src.startsWith('data:')).toBe(false);
    // It must address the plugin's asset route, not a path relative to the client's own /web/ page.
    expect(image.src).toContain('/JellyCrowd/Web/img/');
    expect(image.w).toBeGreaterThan(0);
  }
});

test('the language switch never runs into the overlay close button', async ({ page }) => {
  // The narrow widths are the ones that used to collide: the guide's column fills the panel there.
  for (const width of [1920, 1440, 1100, 960, 820, 600]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`${origin}/web/index.html`);
    await page.locator('.jcGuide .seg').waitFor();

    const boxes = await page.evaluate(() => {
      const seg = document.querySelector('.jcGuide .seg').getBoundingClientRect();
      const close = document.getElementById('jcClose').getBoundingClientRect();
      return { seg: { l: seg.left, r: seg.right, t: seg.top, b: seg.bottom }, close: { l: close.left, r: close.right, t: close.top, b: close.bottom } };
    });
    const overlap = !(boxes.seg.r < boxes.close.l || boxes.seg.l > boxes.close.r
      || boxes.seg.b < boxes.close.t || boxes.seg.t > boxes.close.b);
    expect(overlap, `language switch overlaps the close button at ${width}px`).toBe(false);
  }
});

test('the guide uses the panel width, and its styles stay inside it', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.goto(`${origin}/web/index.html`);
  await page.locator('.jcGuide .hero .wrap').waitFor();

  // It used to render in a 940px column — half of a wide screen, which read as a broken layout.
  const share = await page.evaluate(() =>
    document.querySelector('.jcGuide .hero .wrap').getBoundingClientRect().width / window.innerWidth);
  expect(share).toBeGreaterThan(0.6);

  // No iframe any more: the view states its own typography rather than inheriting the client's. (The
  // guide's own stack ends in sans-serif, so compare the family it starts with, not a substring.)
  const heading = await page.$eval('.jcGuide h1', e => getComputedStyle(e).fontFamily);
  expect(heading.startsWith('system-ui')).toBe(true);
  const outside = await page.$eval('#hostHeading', e => getComputedStyle(e).fontFamily);
  expect(outside).toBe('serif'); // ...and the page around it keeps its own
});

test('the language switch re-renders the guide', async ({ page }) => {
  await page.goto(`${origin}/web/index.html`);
  await page.locator('.jcGuide .seg').waitFor();

  await page.click('.jcGuide .seg button[data-lang="en"]');
  await expect(page.locator('.jcGuide h1')).toContainText('Request movies');
  await page.click('.jcGuide .seg button[data-lang="fr"]');
  await expect(page.locator('.jcGuide h1')).toContainText('Demander des films');
});
