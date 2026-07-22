'use strict';

// Guards the translation catalogs. These checks are cheap and catch the two failure modes that keep
// coming back by hand: a string added to en.json but forgotten in fr.json, and a user-visible label
// written straight into the code instead of going through t().

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const WEB = path.join(__dirname, '..', '..', 'Jellyfin.Plugin.JellyCrowd', 'Web');
const read = name => JSON.parse(fs.readFileSync(path.join(WEB, 'strings', name), 'utf8'));
const en = read('en.json');
const fr = read('fr.json');

const scripts = fs.readdirSync(WEB).filter(f => f.endsWith('.js'));
const sources = new Map(scripts.map(f => [f, fs.readFileSync(path.join(WEB, f), 'utf8')]));

test('every language ships the same keys', () => {
  const missingFr = Object.keys(en).filter(k => !(k in fr));
  const missingEn = Object.keys(fr).filter(k => !(k in en));
  assert.deepStrictEqual(missingFr, [], 'keys missing from fr.json');
  assert.deepStrictEqual(missingEn, [], 'keys missing from en.json');
});

test('no key is left empty', () => {
  for (const [lang, catalog] of [['en', en], ['fr', fr]]) {
    const empty = Object.keys(catalog).filter(k => !String(catalog[k]).trim());
    assert.deepStrictEqual(empty, [], 'empty values in ' + lang + '.json');
  }
});

test('every t() key used in the pages exists in the catalog', () => {
  const unknown = new Set();
  for (const [, src] of sources) {
    // Only literal lookups can be checked; keys built at runtime are skipped on purpose.
    for (const m of src.matchAll(/\bt\(\s*'([a-z0-9_]+)'\s*\)/g)) {
      if (!(m[1] in en)) { unknown.add(m[1]); }
    }
  }
  assert.deepStrictEqual([...unknown], [], 't() keys with no entry in en.json');
});

test('placeholders in a translation match the English string', () => {
  const holders = s => (String(s).match(/\{[a-z]+\}/g) || []).sort();
  const broken = Object.keys(en).filter(k => holders(en[k]).join() !== holders(fr[k]).join());
  assert.deepStrictEqual(broken, [], 'keys whose {placeholders} differ between en and fr');
});

test('user-visible labels are not hardcoded in the admin panel', () => {
  const src = sources.get('admin.js');
  const offenders = [];
  // Declarative form descriptors: { label: '…', hint: '…' }.
  for (const m of src.matchAll(/\b(label|hint):\s*'((?:[^'\\]|\\.){2,})'/g)) {
    if (/[A-Za-z]{2}/.test(m[2])) { offenders.push(m[1] + ": '" + m[2] + "'"); }
  }
  // UI helpers whose first argument is shown to the user.
  for (const m of src.matchAll(/\b(field|sectionHeading|adminBtn)\(\s*'((?:[^'\\]|\\.){2,})'/g)) {
    if (/[A-Za-z]{2}/.test(m[2])) { offenders.push(m[1] + "('" + m[2] + "')"); }
  }
  assert.deepStrictEqual(offenders, [], 'these strings must go through t()');
});
