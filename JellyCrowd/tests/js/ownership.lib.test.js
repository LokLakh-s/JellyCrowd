'use strict';

// Tests for the ownership helpers in catalog.lib.js — the admin screen that lists the library's media with
// their owners and gives or takes them in bulk.

const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');

const lib = require(path.join(__dirname, '..', '..', 'Jellyfin.Plugin.JellyCrowd', 'Web', 'catalog.lib.js'));

const STRINGS = {
  ownership_whole_show: 'whole show',
  ownership_episodes: 'ep. {list}',
  ownership_one_media: '“{title}”',
  ownership_n_media: '{n} media',
  ownership_n_users: '{n} users',
  season_number: 'Season {n}',
  ownership_result_given: 'Given: {n}',
  ownership_result_already: 'already owned: {n}',
  ownership_result_removed: 'Taken away: {n}',
  ownership_result_not_owned: 'not owned: {n}',
  ownership_result_failed: 'not in the library: {n}',
  ownership_result_nothing: 'Nothing changed.'
};
const t = key => (key in STRINGS ? STRINGS[key] : key);

const ITEMS = [
  { MediaType: 'movie', TmdbId: 1, Title: 'Amélie', Owners: [{ Name: 'Bob' }] },
  { MediaType: 'tv', TmdbId: 2, Season: 1, Title: 'Berlin', Owners: [] },
  { MediaType: 'tv', TmdbId: 2, Season: 2, Title: 'Berlin', Owners: [{ Name: 'Alice' }, { Name: 'Zoé' }] },
  { MediaType: 'movie', TmdbId: 3, Title: 'Dune' }
];

test('ownershipKey tells seasons of a show apart, and a whole-show entry from season 0', () => {
  assert.strictEqual(lib.ownershipKey(ITEMS[1]), 'tv:2:1');
  assert.strictEqual(lib.ownershipKey({ MediaType: 'tv', TmdbId: 2, Season: 0 }), 'tv:2:0');
  assert.strictEqual(lib.ownershipKey({ MediaType: 'tv', TmdbId: 2, Season: null }), 'tv:2:');
  assert.strictEqual(lib.ownershipKey(ITEMS[0]), 'movie:1:');
});

test('ownershipCounts counts media with and without owners', () => {
  assert.deepStrictEqual(lib.ownershipCounts(ITEMS), { all: 4, owned: 2, orphans: 2 });
  assert.deepStrictEqual(lib.ownershipCounts(null), { all: 0, owned: 0, orphans: 0 });
});

test('filterOwnership keeps owned or orphan media', () => {
  assert.deepStrictEqual(lib.filterOwnership(ITEMS, '', 'owned').map(it => it.TmdbId), [1, 2]);
  assert.deepStrictEqual(lib.filterOwnership(ITEMS, '', 'orphans').map(it => lib.ownershipKey(it)), ['tv:2:1', 'movie:3:']);
  assert.strictEqual(lib.filterOwnership(ITEMS, '', 'all').length, 4);
});

test('filterOwnership matches the title or an owner, ignoring case and accents', () => {
  assert.deepStrictEqual(lib.filterOwnership(ITEMS, 'amelie', 'all').map(it => it.TmdbId), [1]);
  assert.deepStrictEqual(lib.filterOwnership(ITEMS, ' ZOE ', 'all').map(it => lib.ownershipKey(it)), ['tv:2:2']);
  assert.deepStrictEqual(lib.filterOwnership(ITEMS, 'berlin', 'orphans').map(it => lib.ownershipKey(it)), ['tv:2:1']);
  assert.deepStrictEqual(lib.filterOwnership(ITEMS, 'nothing', 'all'), []);
});

test('ownershipHolding says when a season is held through the whole show or a few episodes', () => {
  assert.strictEqual(lib.ownershipHolding({ WholeShow: true, Episodes: [] }, t), 'whole show');
  assert.strictEqual(lib.ownershipHolding({ WholeShow: false, Episodes: [1, 3] }, t), 'ep. 1, 3');
  assert.strictEqual(lib.ownershipHolding({ WholeShow: false, Episodes: [] }, t), '');
});

test('ownershipChangeBody sends what the server needs to create ownerships', () => {
  const body = lib.ownershipChangeBody([ITEMS[2], ITEMS[0]], ['u1', 'u2']);
  assert.deepStrictEqual(body, {
    UserIds: ['u1', 'u2'],
    Media: [
      { MediaType: 'tv', TmdbId: 2, Title: 'Berlin', PosterPath: null, Season: 2 },
      { MediaType: 'movie', TmdbId: 1, Title: 'Amélie', PosterPath: null, Season: null }
    ]
  });
});

test('ownershipSubject names one media, counts several', () => {
  assert.strictEqual(lib.ownershipSubject([ITEMS[0]], t), '“Amélie”');
  assert.strictEqual(lib.ownershipSubject([ITEMS[2]], t), '“Berlin” (Season 2)');
  assert.strictEqual(lib.ownershipSubject(ITEMS, t), '4 media');
});

test('ownershipUsersLabel names up to three users', () => {
  assert.strictEqual(lib.ownershipUsersLabel(['a', 'b', 'c'], t), 'a, b, c');
  assert.strictEqual(lib.ownershipUsersLabel(['a', 'b', 'c', 'd'], t), '4 users');
});

test('ownershipResultMessage lists only what happened', () => {
  assert.strictEqual(lib.ownershipResultMessage({ Given: 3, AlreadyOwned: 1, Removed: 0, NotOwned: 0, Failed: 0 }, t), 'Given: 3 · already owned: 1');
  assert.strictEqual(lib.ownershipResultMessage({ Removed: 2, NotOwned: 1 }, t), 'Taken away: 2 · not owned: 1');
  assert.strictEqual(lib.ownershipResultMessage({ Given: 0, Failed: 2 }, t), 'not in the library: 2');
  assert.strictEqual(lib.ownershipResultMessage({}, t), 'Nothing changed.');
});
