'use strict';

/*
 * Scale check against a REAL Jellyfin: how do the hot endpoints behave as the request store grows?
 *
 * Several paths walk the whole store on every call (the quota is recomputed from every request a user
 * owns; the catalog marks its rows against every request). That is fine at 123 requests — the question is
 * what it costs at 10k or 50k, i.e. whether this degrades gracefully or falls off a cliff.
 *
 * Usage: BASE=http://host:8096 CONTAINER=jc-e2e-jellyfin node perf.js
 */

const { execFileSync } = require('node:child_process');

const BASE = process.env.BASE || 'http://localhost:8096';
const CONTAINER = process.env.CONTAINER || 'jc-e2e-jellyfin';
const SIZES = (process.env.SIZES || '100,1000,10000,50000').split(',').map(Number);
const ADMIN = 'e2e-admin';
const USER = 'e2e-user';
const PW = 'e2e-Passw0rd!';

// Jellyfin 12 dropped the legacy X-Emby-Authorization header (it answers 400); the standard
// Authorization header carrying the same MediaBrowser scheme is accepted by 10.11 and 12 alike.
const auth = (t) => `MediaBrowser Client="perf", Device="perf", DeviceId="perf-1", Version="1.0"${t ? `, Token="${t}"` : ''}`;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function api(path, { method = 'GET', body, token } = {}) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', 'Authorization': auth(token) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let json = null;
  try { json = await res.clone().json(); } catch { /* ignore */ }
  return { status: res.status, json };
}

async function setup() {
  for (let i = 0; i < 40; i++) {
    const probe = await api('/Startup/User');
    if (probe.status === 200 || probe.status === 403) break;
    await sleep(1000);
  }
  await api('/Startup/Configuration', { method: 'POST', body: { UICulture: 'en-US', MetadataCountryCode: 'FR', PreferredMetadataLanguage: 'en' } });
  await api('/Startup/User', { method: 'POST', body: { Name: ADMIN, Password: PW } });
  await api('/Startup/Complete', { method: 'POST' });

  const a = await api('/Users/AuthenticateByName', { method: 'POST', body: { Username: ADMIN, Pw: PW } });
  const admin = { token: a.json.AccessToken, id: a.json.User.Id };
  await api('/Users/New', { method: 'POST', token: admin.token, body: { Name: USER, Password: PW } });
  const u = await api('/Users/AuthenticateByName', { method: 'POST', body: { Username: USER, Pw: PW } });
  return { admin, user: { token: u.json.AccessToken, id: u.json.User.Id } };
}

// A store of `count` requests spread over `users` owners — the shape that actually stresses the code:
// mostly Available (each is an ownership row the quota must sum) and per-user, since the quota is per-user.
function buildStore(count, userIds) {
  const items = [];
  for (let i = 0; i < count; i++) {
    items.push({
      Id: `00000000-0000-0000-0000-${String(i).padStart(12, '0')}`,
      UserId: userIds[i % userIds.length],
      TmdbId: 1000 + i,
      MediaType: i % 3 === 0 ? 'tv' : 'movie',
      Title: `Title ${i}`,
      Status: 3, // Available: the ownership rows the quota is computed from
      RequestedAt: '2026-01-01T00:00:00Z',
      AvailableAt: '2026-01-02T00:00:00Z',
      JellyfinItemId: `item-${i}`,
      Season: i % 3 === 0 ? 1 : null,
    });
  }

  return JSON.stringify({ SchemaVersion: 1, Items: items });
}

// The plugin's data folder is named after its assembly, as in production.
const DATA_DIR = '/config/plugins/Jellyfin.Plugin.JellyCrowd';

function writeStore(json) {
  // Straight into the plugin's data folder. The store caches in memory, so the caller restarts Jellyfin
  // afterwards to make it re-read the file.
  execFileSync('docker', ['exec', '-i', CONTAINER, 'sh', '-lc',
    `mkdir -p '${DATA_DIR}' && cat > '${DATA_DIR}/requests.json'`], { input: json });
}

async function timeIt(path, token, n = 5) {
  const ts = [];
  for (let i = 0; i < n; i++) {
    const t = process.hrtime.bigint();
    const res = await fetch(BASE + path, { headers: { 'Authorization': auth(token) } });
    await res.arrayBuffer();
    ts.push(Number(process.hrtime.bigint() - t) / 1e6);
  }
  ts.sort((a, b) => a - b);
  return ts[Math.floor(n / 2)];
}

(async () => {
  const { admin, user } = await setup();
  const userIds = [user.id, admin.id];

  console.log('\nrequests |  Quota/Me | Requests/Mine | Quota/All (admin) | Requests (admin)');
  console.log('---------|-----------|---------------|-------------------|-----------------');

  for (const size of SIZES) {
    writeStore(buildStore(size, userIds));
    execFileSync('docker', ['restart', CONTAINER]);
    for (let i = 0; i < 90; i++) {
      try {
        const s = execFileSync('docker', ['inspect', '--format', '{{.State.Health.Status}}', CONTAINER], { encoding: 'utf8' }).trim();
        if (s === 'healthy') break;
      } catch { /* ignore */ }
      await sleep(2000);
    }

    const q = await timeIt('/JellyCrowd/Quota/Me', user.token);
    const m = await timeIt('/JellyCrowd/Requests/Mine', user.token);
    const qa = await timeIt('/JellyCrowd/Quota/All', admin.token);
    const ra = await timeIt('/JellyCrowd/Requests', admin.token);
    const f = (v) => `${v.toFixed(0)} ms`.padStart(9);
    console.log(`${String(size).padStart(8)} | ${f(q)} | ${f(m).padStart(13)} | ${f(qa).padStart(17)} | ${f(ra).padStart(16)}`);
  }
})().catch((e) => { console.error('perf harness error:', e.message); process.exit(1); });
