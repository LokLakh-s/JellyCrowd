'use strict';

// Stacking order of jellycrowd.css against the native Jellyfin header. The detail modal once sat at
// z-index 1000, under the MUI app bar of Jellyfin 12 (1100): on phones the header icons covered the
// modal's close button, leaving no way out (issue #16).

const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const css = fs.readFileSync(path.join(
  __dirname,
  '..',
  '..',
  'Jellyfin.Plugin.JellyCrowd',
  'Web',
  'jellycrowd.css'), 'utf8');

// z-index declared by the top-level rule whose selector is exactly `selector`.
function zIndexOf(selector) {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const rule = new RegExp('(?:^|\\})\\s*' + escaped + '\\s*\\{([^}]*)\\}', 'm').exec(css);
  assert.ok(rule, 'rule not found: ' + selector);
  const z = /z-index:\s*(\d+)/.exec(rule[1]);
  assert.ok(z, 'no z-index on ' + selector);
  return Number(z[1]);
}

// Native layers, as shipped by jellyfin-web: classic .skinHeader (10.11) and the MUI theme defaults
// (Jellyfin 12 does not override them).
const SKIN_HEADER = 999;
const MUI_MODAL = 1300;
// The header popovers the plugin draws itself (avatar menu is the lowest of them).
const HEADER_POPOVERS = 10000;

test('the detail modal sits above the native header, 10.11 and 12', () => {
  const z = zIndexOf('.jellycrowd-modal-overlay');
  assert.ok(z > SKIN_HEADER, 'under the classic skinHeader: ' + z);
  assert.ok(z > MUI_MODAL, 'under the MUI app bar/drawer/modal layers: ' + z);
});

test('the detail modal stays under the header popovers', () => {
  assert.ok(zIndexOf('.jellycrowd-modal-overlay') < HEADER_POPOVERS);
});

test('the plugin panel stays under the native header, which remains usable above it', () => {
  assert.ok(zIndexOf('.jellycrowd-overlay') < SKIN_HEADER);
});

test('the poll takeover still wins over the detail modal', () => {
  assert.ok(zIndexOf('.jcPollModal') > zIndexOf('.jellycrowd-modal-overlay'));
});
