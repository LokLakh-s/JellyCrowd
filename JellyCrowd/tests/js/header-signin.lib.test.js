'use strict';

// Tests for the header helpers in catalog.lib.js that handle a sign-in made from Jellyfin's login page,
// without a reload: what a change of user means, and when our icons sit in front of the native ones.

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

test('a first sign-in refetches the per-user parts', () => {
  assert.strictEqual(lib.signInChange('', 'u1'), 'signed-in');
  assert.strictEqual(lib.signInChange(null, 'u1'), 'signed-in');
});

test('the same user, or nobody signed in, changes nothing', () => {
  assert.strictEqual(lib.signInChange('u1', 'u1'), 'none');
  assert.strictEqual(lib.signInChange('u1', ''), 'none', 'signing out waits for the next sign-in');
  assert.strictEqual(lib.signInChange('', ''), 'none');
  assert.strictEqual(lib.signInChange('', null), 'none');
});

test('another account signing in in the same tab reloads', () => {
  assert.strictEqual(lib.signInChange('u1', 'u2'), 'switched');
});

test('native icons rendered after ours put them out of order', () => {
  // What a sign-in from the login page leaves: ours first, then SyncPlay, Cast, Search.
  assert.strictEqual(lib.headerClusterMisplaced(['ours', 'ours', 'ours', 'native', 'native', 'native']), true);
  assert.strictEqual(lib.headerClusterMisplaced(['native', 'ours', 'native', 'ours']), true);
});

test('native icons first, as after a reload, is the order we want', () => {
  assert.strictEqual(lib.headerClusterMisplaced(['native', 'native', 'native', 'ours', 'ours', 'ours']), false);
  assert.strictEqual(lib.headerClusterMisplaced(['native', 'native']), false, 'nothing of ours yet');
  assert.strictEqual(lib.headerClusterMisplaced(['ours', 'ours']), false, 'no native icon yet');
  assert.strictEqual(lib.headerClusterMisplaced([]), false);
  assert.strictEqual(lib.headerClusterMisplaced(undefined), false);
});

test("10.11's avatar stays last: what follows it does not count", () => {
  assert.strictEqual(lib.headerClusterMisplaced(['native', 'ours', 'anchor', 'native']), false);
  assert.strictEqual(lib.headerClusterMisplaced(['ours', 'native', 'anchor']), true);
});
