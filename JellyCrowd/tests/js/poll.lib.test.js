'use strict';

// Tests for the poll helpers in catalog.lib.js — the decisions behind the header badge and the
// once-per-session takeover (which poll to prompt, what "later" remembers, when a ballot is valid).

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

// Stands in for sessionStorage, including the private-mode case where every access throws.
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

const poll = (over = {}) => Object.assign({ Id: 'p1', CanVote: true, Voted: false, MultiChoice: false }, over);

test('a ballot needs a pick, and a single-choice poll takes exactly one', () => {
  assert.strictEqual(lib.pollSelectionValid(poll(), []), false);
  assert.strictEqual(lib.pollSelectionValid(poll(), ['a']), true);
  assert.strictEqual(lib.pollSelectionValid(poll(), ['a', 'b']), false);
  assert.strictEqual(lib.pollSelectionValid(poll({ MultiChoice: true }), ['a', 'b']), true);
});

test('single choice replaces the selection, multi choice toggles it', () => {
  assert.deepStrictEqual(lib.pollToggleSelection(['a'], 'b', false), ['b']);
  assert.deepStrictEqual(lib.pollToggleSelection(['a'], 'a', false), [], 'clicking the picked option clears it');
  assert.deepStrictEqual(lib.pollToggleSelection(['a'], 'b', true), ['a', 'b']);
  assert.deepStrictEqual(lib.pollToggleSelection(['a', 'b'], 'a', true), ['b']);
});

test('the badge counts only the polls this user can still answer', () => {
  const polls = [
    poll({ Id: 'open' }),
    poll({ Id: 'voted', Voted: true }),
    poll({ Id: 'closed', CanVote: false })
  ];
  assert.deepStrictEqual(lib.pollsAwaitingAnswer(polls).map(p => p.Id), ['open']);
  assert.deepStrictEqual(lib.pollsAwaitingAnswer(null), []);
});

test('"later" holds for the session, and the next poll is prompted meanwhile', () => {
  const polls = [poll({ Id: 'p1' }), poll({ Id: 'p2' })];
  const s = store();
  assert.strictEqual(lib.pollToPrompt(polls, s).Id, 'p1');

  lib.pollDismiss(s, 'p1');
  assert.strictEqual(lib.pollDismissed(s, 'p1'), true);
  assert.strictEqual(lib.pollToPrompt(polls, s).Id, 'p2', 'the pushed-back poll steps aside, it does not block the rest');

  lib.pollDismiss(s, 'p2');
  assert.strictEqual(lib.pollToPrompt(polls, s), null);
});

test('voting ends the prompt for good, whatever the session remembers', () => {
  const s = store();
  assert.strictEqual(lib.pollToPrompt([poll({ Voted: true })], s), null);
  assert.strictEqual(lib.pollToPrompt([poll({ CanVote: false })], s), null);
});

test('a storage that throws never breaks the prompt', () => {
  // Private windows and blocked site data throw on access: the takeover must still show, not crash.
  assert.strictEqual(lib.pollDismissed(throwingStore, 'p1'), false);
  assert.doesNotThrow(() => lib.pollDismiss(throwingStore, 'p1'));
  assert.strictEqual(lib.pollToPrompt([poll()], throwingStore).Id, 'p1');
  assert.strictEqual(lib.pollToPrompt([poll()], null).Id, 'p1');
});

test('bar widths are clamped and rounded', () => {
  assert.strictEqual(lib.pollBarPercent({ Percent: 33.4 }), 33);
  assert.strictEqual(lib.pollBarPercent({ Percent: 120 }), 100, 'multi-choice shares can exceed 100 together, never alone');
  assert.strictEqual(lib.pollBarPercent({ Percent: -5 }), 0);
  assert.strictEqual(lib.pollBarPercent({}), 0);
  assert.strictEqual(lib.pollBarPercent(null), 0);
});
