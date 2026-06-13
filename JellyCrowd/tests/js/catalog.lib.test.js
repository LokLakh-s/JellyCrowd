'use strict';

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

const SUPPORTED = ['en', 'fr'];

test('pickLang returns the matching 2-letter code', () => {
  assert.strictEqual(lib.pickLang('fr-FR', SUPPORTED), 'fr');
  assert.strictEqual(lib.pickLang('en-US', SUPPORTED), 'en');
});

test('pickLang falls back to en for unsupported or empty locales', () => {
  assert.strictEqual(lib.pickLang('de-DE', SUPPORTED), 'en');
  assert.strictEqual(lib.pickLang('', SUPPORTED), 'en');
  assert.strictEqual(lib.pickLang(null, SUPPORTED), 'en');
});

test('resolveLang honors a supported admin-forced language', () => {
  assert.strictEqual(lib.resolveLang('fr', SUPPORTED, 'en-US'), 'fr');
  assert.strictEqual(lib.resolveLang('EN', SUPPORTED, 'fr-FR'), 'en');
});

test('resolveLang follows the user locale in auto mode or when forced lang is unsupported', () => {
  assert.strictEqual(lib.resolveLang('auto', SUPPORTED, 'fr-FR'), 'fr');
  assert.strictEqual(lib.resolveLang('auto', SUPPORTED, 'de-DE'), 'en');
  assert.strictEqual(lib.resolveLang('de', SUPPORTED, 'fr-FR'), 'fr');
  assert.strictEqual(lib.resolveLang(null, SUPPORTED, 'fr-FR'), 'fr');
});

test('contentLocale maps a forced language to a full TMDB locale', () => {
  assert.strictEqual(lib.contentLocale('fr', 'en-US'), 'fr-FR');
  assert.strictEqual(lib.contentLocale('en', 'fr-FR'), 'en-US');
});

test('contentLocale keeps the user locale in auto mode', () => {
  assert.strictEqual(lib.contentLocale('auto', 'es-ES'), 'es-ES');
  assert.strictEqual(lib.contentLocale('auto', ''), 'en-US');
});

test('isoDate formats a Date as zero-padded YYYY-MM-DD (local)', () => {
  assert.strictEqual(lib.isoDate(new Date(2026, 0, 5)), '2026-01-05');
  assert.strictEqual(lib.isoDate(new Date(2026, 11, 31)), '2026-12-31');
});

test('yearOf extracts the year or returns empty', () => {
  assert.strictEqual(lib.yearOf({ ReleaseDate: '2021-02-02' }), '2021');
  assert.strictEqual(lib.yearOf({}), '');
  assert.strictEqual(lib.yearOf(null), '');
});

test('formatTitle appends the year when known', () => {
  assert.strictEqual(lib.formatTitle({ Title: 'Dune', ReleaseDate: '2021-10-22' }), 'Dune (2021)');
  assert.strictEqual(lib.formatTitle({ Title: 'No Date' }), 'No Date');
});

test('errorKey maps 503 to the not-configured message', () => {
  assert.strictEqual(lib.errorKey(503), 'error_not_configured');
  assert.strictEqual(lib.errorKey(500), 'error_generic');
  assert.strictEqual(lib.errorKey(undefined), 'error_generic');
});

test('formatRating returns one decimal or empty', () => {
  assert.strictEqual(lib.formatRating(7.5), '7.5');
  assert.strictEqual(lib.formatRating(8), '8.0');
  assert.strictEqual(lib.formatRating(0), '');
  assert.strictEqual(lib.formatRating(undefined), '');
});

test('statusLabelKey handles numeric and string statuses', () => {
  assert.strictEqual(lib.statusLabelKey(0), 'status_pending');
  assert.strictEqual(lib.statusLabelKey(1), 'status_approved');
  assert.strictEqual(lib.statusLabelKey(2), 'status_denied');
  assert.strictEqual(lib.statusLabelKey(3), 'status_available');
  assert.strictEqual(lib.statusLabelKey('Approved'), 'status_approved');
  assert.strictEqual(lib.statusLabelKey('weird'), 'status_pending');
});

test('statusRank orders pending < approved < available < denied', () => {
  assert.strictEqual(lib.statusRank(0), 0);
  assert.strictEqual(lib.statusRank('Pending'), 0);
  assert.strictEqual(lib.statusRank(1), 1);
  assert.strictEqual(lib.statusRank('Approved'), 1);
  assert.strictEqual(lib.statusRank(3), 2);
  assert.strictEqual(lib.statusRank('Available'), 2);
  assert.strictEqual(lib.statusRank(2), 3);
  assert.ok(lib.statusRank(0) < lib.statusRank(1));
  assert.ok(lib.statusRank(1) < lib.statusRank(3));
  assert.ok(lib.statusRank(3) < lib.statusRank(2));
});

test('orderPair returns sorted [min, max]', () => {
  assert.deepStrictEqual(lib.orderPair(2000, 2020), [2000, 2020]);
  assert.deepStrictEqual(lib.orderPair(2020, 2000), [2000, 2020]);
  assert.deepStrictEqual(lib.orderPair(5, 5), [5, 5]);
});

test('formatBytes renders binary units', () => {
  assert.strictEqual(lib.formatBytes(0), '0 B');
  assert.strictEqual(lib.formatBytes(1024), '1.0 KiB');
  assert.strictEqual(lib.formatBytes(5 * 1024 * 1024 * 1024), '5.0 GiB');
});

test('quotaPercent clamps and treats <=0 quota as unlimited', () => {
  assert.strictEqual(lib.quotaPercent(5, 10), 50);
  assert.strictEqual(lib.quotaPercent(15, 10), 100);
  assert.strictEqual(lib.quotaPercent(1, 0), 0);
});
