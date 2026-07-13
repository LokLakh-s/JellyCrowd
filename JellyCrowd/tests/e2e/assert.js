'use strict';

/*
 * Drives a freshly-booted Jellyfin through its setup wizard, then asserts the integration surface that
 * unit tests cannot reach: does the plugin actually LOAD, do its services RESOLVE out of Jellyfin's DI,
 * do its routes answer, are its scheduled tasks registered, does authorization hold against a real
 * (non-admin) user, and is the web shell injected into the client?
 *
 * A regression in any of these is invisible to the unit suite and fatal in production.
 */

const BASE = process.env.BASE || 'http://localhost:8096';
const ADMIN = 'e2e-admin';
const USER = 'e2e-user';
const PW = 'e2e-Passw0rd!';

let pass = 0;
let fail = 0;
const ok = (m) => { console.log(`  \x1b[32m✓\x1b[0m ${m}`); pass++; };
const ko = (m) => { console.log(`  \x1b[31m✗\x1b[0m ${m}`); fail++; };
const check = (cond, m) => (cond ? ok(m) : ko(m));

const auth = (token) =>
  `MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-1", Version="1.0"` + (token ? `, Token="${token}"` : '');

async function api(path, { method = 'GET', body, token } = {}) {
  const res = await fetch(BASE + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      'X-Emby-Authorization': auth(token),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let json = null;
  try { json = await res.clone().json(); } catch { /* not json */ }
  return { status: res.status, ok: res.ok, json, text: async () => res.text() };
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function completeWizard() {
  // /System/Info/Public answers before the startup routes are mapped, so "the server responds" is not the
  // same as "the wizard is ready" — poll the wizard itself, then run it. Re-running against an already
  // configured server just gets rejected, which is harmless.
  for (let i = 0; i < 30; i++) {
    const probe = await api('/Startup/User');
    if (probe.status === 200 || probe.status === 403) { break; }
    await sleep(1000);
  }

  await api('/Startup/Configuration', { method: 'POST', body: { UICulture: 'en-US', MetadataCountryCode: 'FR', PreferredMetadataLanguage: 'en' } });
  await api('/Startup/User', { method: 'POST', body: { Name: ADMIN, Password: PW } });
  await api('/Startup/RemoteAccess', { method: 'POST', body: { EnableRemoteAccess: true, EnableAutomaticPortMapping: false } });
  await api('/Startup/Complete', { method: 'POST' });
}

async function login(name, password) {
  const res = await api('/Users/AuthenticateByName', { method: 'POST', body: { Username: name, Pw: password } });
  return res.json?.AccessToken ? { token: res.json.AccessToken, userId: res.json.User.Id } : null;
}

(async () => {
  console.log('\n\x1b[1mSetup wizard\x1b[0m');
  let admin = null;
  for (let attempt = 1; attempt <= 3 && !admin; attempt++) {
    await completeWizard();
    admin = await login(ADMIN, PW);
    if (!admin) { await sleep(3000); }
  }

  check(!!admin, 'admin account created and authenticates');
  if (!admin) { process.exit(1); }

  // A NON-admin user: authorization is the thing most easily broken, and only a real one proves it holds.
  // Creating it may 4xx on a re-run against a kept container — what matters is that it can authenticate.
  await api('/Users/New', { method: 'POST', token: admin.token, body: { Name: USER, Password: PW } });
  const user = await login(USER, PW);
  check(!!user, 'an ordinary (non-admin) user exists and authenticates');
  if (!user) { process.exit(1); }

  console.log('\n\x1b[1mThe plugin loaded into Jellyfin\x1b[0m');
  const plugins = await api('/Plugins', { token: admin.token });
  const jc = (plugins.json || []).find((p) => /jelly ?crowd/i.test(p.Name || ''));
  check(!!jc, `plugin is loaded${jc ? ` (${jc.Name} ${jc.Version})` : ''}`);
  check(jc?.Status === 'Active', 'plugin status is Active (it did not fail to start)');

  console.log('\n\x1b[1mIts services resolved out of Jellyfin\'s DI\x1b[0m');
  // A DI failure (a service the registrator forgot, a cycle) does not crash the server — it makes the
  // scheduled tasks vanish and every route 500. Both are asserted below.
  const tasks = await api('/ScheduledTasks', { token: admin.token });
  const mine = (tasks.json || []).filter((t) => /jelly ?crowd/i.test(t.Category || t.Name || ''));
  check(mine.length > 0, `scheduled tasks registered (${mine.length}: ${mine.map((t) => t.Key).join(', ')})`);
  check(mine.some((t) => t.Key === 'JellyCrowdAnalyzeOutros'), 'the outro analysis task is registered');

  console.log('\n\x1b[1mIts routes answer\x1b[0m');
  const lang = await api('/JellyCrowd/Settings/Language');
  check(lang.status === 200, 'GET Settings/Language (anonymous) → 200');

  const quota = await api('/JellyCrowd/Quota/Me', { token: user.token });
  check(quota.status === 200, 'GET Quota/Me (as a user) → 200');
  check(typeof quota.json?.QuotaBytes === 'number', 'Quota/Me returns a real quota payload');

  const requests = await api('/JellyCrowd/Requests/Mine', { token: user.token });
  check(requests.status === 200, 'GET Requests/Mine (as a user) → 200');
  check(Array.isArray(requests.json), 'Requests/Mine returns a list');

  console.log('\n\x1b[1mAuthorization holds against a real non-admin\x1b[0m');
  // GET /JellyCrowd/Requests (no suffix) is the admin listing — RequiresElevation.
  const all = await api('/JellyCrowd/Requests', { token: user.token });
  check(all.status === 401 || all.status === 403, `a user cannot read everyone's requests (got ${all.status})`);

  const asAdmin = await api('/JellyCrowd/Requests', { token: admin.token });
  check(asAdmin.status === 200, `an admin can (got ${asAdmin.status})`);

  const noToken = await api('/JellyCrowd/Requests/Mine');
  check(noToken.status === 401, `an unauthenticated caller is rejected (got ${noToken.status})`);

  console.log('\n\x1b[1mThe web shell is injected into the client\x1b[0m');
  const index = await fetch(BASE + '/web/index.html');
  const html = await index.text();
  check(index.ok, 'GET /web/index.html → 200');
  check(/JellyCrowd\/Web\/header\.js/i.test(html), 'index.html carries the header.js <script> (the middleware ran)');

  const headerJs = await fetch(BASE + '/JellyCrowd/Web/header.js');
  check(headerJs.ok, 'GET /JellyCrowd/Web/header.js → 200 (the embedded asset is served)');

  console.log(`\n\x1b[1mE2E: ${pass} passed, ${fail} failed\x1b[0m`);
  process.exit(fail === 0 ? 0 : 1);
})().catch((e) => {
  console.error('E2E harness error:', e.message);
  process.exit(1);
});
