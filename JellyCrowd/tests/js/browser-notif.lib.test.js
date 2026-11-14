'use strict';

// Tests for the browser-notification helpers in catalog.lib.js (issue #28): which bell entries pop up
// after a refresh, the per-device switch, and what the Preferences page shows for each permission.

const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');

const lib = require(path.join(
  __dirname,
  '..',
  '..',
  'Jellyfin.Plugin.JellyCrowd',
  'Web',
  'catalog.lib.js'));

// Stands in for localStorage, including the private-mode case where every access throws.
function store(initial) {
  const data = Object.assign({}, initial);
  return {
    getItem: k => (k in data ? data[k] : null),
    setItem: (k, v) => { data[k] = String(v); },
    data
  };
}

const throwingStore = {
  getItem() { throw new Error('blocked'); },
  setItem() { throw new Error('blocked'); }
};

function entry(id, createdAt, extra) {
  return Object.assign({ Id: id, Title: 'T' + id, Message: 'M' + id, CreatedAt: createdAt, Read: false }, extra);
}

const T0 = '2026-11-14T10:00:00Z';
const T1 = '2026-11-14T10:01:00Z';
const T2 = '2026-11-14T10:02:00Z';
const T3 = '2026-11-14T10:03:00Z';
const T4 = '2026-11-14T10:04:00Z';

test('the first refresh only arms the baseline: what is already in the bell does not pop up', () => {
  const plan = lib.browserNotificationPlan([entry('a', T1), entry('b', T0)], null, 3);
  assert.deepStrictEqual(plan.show, []);
  assert.strictEqual(plan.more, 0);
  assert.strictEqual(plan.baseline, Date.parse(T1));
});

test('an empty bell on the first refresh still arms the next one', () => {
  const first = lib.browserNotificationPlan([], null, 3);
  assert.strictEqual(first.baseline, 0);
  const next = lib.browserNotificationPlan([entry('a', T0)], first.baseline, 3);
  assert.deepStrictEqual(next.show.map(n => n.Id), ['a']);
});

test('entries newer than the baseline pop up, oldest first, and move it forward', () => {
  // The server lists the newest first.
  const items = [entry('c', T2), entry('b', T1), entry('a', T0)];
  const plan = lib.browserNotificationPlan(items, Date.parse(T0), 3);
  assert.deepStrictEqual(plan.show.map(n => n.Id), ['b', 'c']);
  assert.strictEqual(plan.baseline, Date.parse(T2));

  // The same list on the next refresh shows nothing again.
  assert.deepStrictEqual(lib.browserNotificationPlan(items, plan.baseline, 3).show, []);
});

test('entries already read elsewhere are skipped but still move the baseline', () => {
  const plan = lib.browserNotificationPlan([entry('b', T1, { Read: true })], Date.parse(T0), 3);
  assert.deepStrictEqual(plan.show, []);
  assert.strictEqual(plan.baseline, Date.parse(T1));
});

test('past the cap, the newest entries are shown and the rest is counted', () => {
  const items = [entry('e', T4), entry('d', T3), entry('c', T2), entry('b', T1)];
  const plan = lib.browserNotificationPlan(items, Date.parse(T0), 2);
  assert.deepStrictEqual(plan.show.map(n => n.Id), ['d', 'e']);
  assert.strictEqual(plan.more, 2);
  assert.strictEqual(plan.baseline, Date.parse(T4));
});

test('the cap defaults to three', () => {
  const items = [entry('e', T4), entry('d', T3), entry('c', T2), entry('b', T1)];
  const plan = lib.browserNotificationPlan(items, Date.parse(T0));
  assert.strictEqual(plan.show.length, 3);
  assert.strictEqual(plan.more, 1);
});

test('entries without a readable date and a missing list are ignored', () => {
  const plan = lib.browserNotificationPlan([entry('x', 'not a date'), null, entry('b', T1)], Date.parse(T0), 3);
  assert.deepStrictEqual(plan.show.map(n => n.Id), ['b']);
  assert.deepStrictEqual(lib.browserNotificationPlan(undefined, 5, 3), { show: [], more: 0, baseline: 5 });
});

test('the switch is on by default, per user, and survives unreadable storage', () => {
  const s = store();
  assert.strictEqual(lib.browserNotifEnabled(s, 'u1'), true);
  lib.setBrowserNotifEnabled(s, 'u1', false);
  assert.strictEqual(lib.browserNotifEnabled(s, 'u1'), false);
  assert.strictEqual(lib.browserNotifEnabled(s, 'u2'), true, 'another user on the same browser keeps their own');
  lib.setBrowserNotifEnabled(s, 'u1', true);
  assert.strictEqual(lib.browserNotifEnabled(s, 'u1'), true);
  assert.strictEqual(s.data[lib.browserNotifKey('u1')], '1');

  assert.strictEqual(lib.browserNotifEnabled(throwingStore, 'u1'), true);
  assert.strictEqual(lib.browserNotifEnabled(null, 'u1'), true);
  assert.doesNotThrow(() => lib.setBrowserNotifEnabled(throwingStore, 'u1', false));
});

test('the Preferences switch follows the browser permission', () => {
  assert.deepStrictEqual(lib.browserNotifSwitch('granted', true), { checked: true, disabled: false, hintKey: 'prefs_browser_hint' });
  assert.deepStrictEqual(lib.browserNotifSwitch('granted', false), { checked: false, disabled: false, hintKey: 'prefs_browser_hint' });
  // Not asked yet: off until the browser says yes, and turning it on is what asks.
  assert.deepStrictEqual(lib.browserNotifSwitch('default', true), { checked: false, disabled: false, hintKey: 'prefs_browser_ask' });
  // Refused: only the browser's site settings can give it back.
  assert.deepStrictEqual(lib.browserNotifSwitch('denied', true), { checked: false, disabled: true, hintKey: 'prefs_browser_denied' });
  // No Notification API at all (some apps wrapping the web client).
  assert.deepStrictEqual(lib.browserNotifSwitch(null, true), { checked: false, disabled: true, hintKey: 'prefs_browser_unsupported' });
});

test('every hint the switch can show exists in the catalogs', () => {
  const fs = require('node:fs');
  const dir = path.join(__dirname, '..', '..', 'Jellyfin.Plugin.JellyCrowd', 'Web', 'strings');
  const en = JSON.parse(fs.readFileSync(path.join(dir, 'en.json'), 'utf8'));
  const fr = JSON.parse(fs.readFileSync(path.join(dir, 'fr.json'), 'utf8'));
  for (const permission of ['granted', 'default', 'denied', null]) {
    const key = lib.browserNotifSwitch(permission, true).hintKey;
    assert.ok(en[key] && fr[key], key + ' missing from a catalog');
  }
});

test('a pop-up shows the poster when the entry has one, else the logo', () => {
  assert.strictEqual(lib.browserNotificationIcon({ PosterPath: '/abc.jpg' }, 'logo.png'), 'https://image.tmdb.org/t/p/w154/abc.jpg');
  assert.strictEqual(lib.browserNotificationIcon({ PosterPath: null }, 'logo.png'), 'logo.png');
  assert.strictEqual(lib.browserNotificationIcon(null, 'logo.png'), 'logo.png');
});
