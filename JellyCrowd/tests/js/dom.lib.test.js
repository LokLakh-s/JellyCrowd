'use strict';

// DOM tests (jsdom) for the framework-free DOM helpers in catalog.lib.js — the modal focus trap that
// ships in catalog.js. jsdom has no layout engine, so getClientRects()/offsetParent are always empty;
// the helpers take an injectable `visible` predicate precisely so they stay testable here while using
// the real layout check in the browser.

const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const lib = require(path.join(
  __dirname,
  '..',
  '..',
  'Jellyfin.Plugin.JellyCrowd',
  'Web',
  'catalog.lib.js'));

// Treat everything not explicitly `hidden` as visible (stands in for the browser's getClientRects check).
const VISIBLE = { visible: function (el) { return !el.hidden; } };

function setup(html) {
  return new JSDOM('<!DOCTYPE html><body>' + html + '</body>').window.document;
}

function keyEvent(key, shift) {
  const e = { key: key, shiftKey: !!shift, prevented: false };
  e.preventDefault = function () { e.prevented = true; };
  return e;
}

function trapModal() {
  const doc = setup(
    '<div id="m">'
    + '<button id="first">f</button><button id="mid">m</button><button id="last">l</button>'
    + '</div><button id="outside">o</button>');
  return {
    doc: doc,
    modal: doc.getElementById('m'),
    first: doc.getElementById('first'),
    mid: doc.getElementById('mid'),
    last: doc.getElementById('last'),
    outside: doc.getElementById('outside')
  };
}

test('focusablesIn returns the visible focusable elements in DOM order', () => {
  const doc = setup(
    '<div id="m">'
    + '<a href="#">a</a>'
    + '<button>b</button>'
    + '<button disabled>x</button>'
    + '<div tabindex="0">c</div>'
    + '<span>plain</span>'
    + '<input>'
    + '<input tabindex="-1">'
    + '</div>');
  const f = lib.focusablesIn(doc.getElementById('m'), VISIBLE);
  assert.deepStrictEqual(f.map(function (el) { return el.tagName.toLowerCase(); }), ['a', 'button', 'div', 'input']);
});

test('focusablesIn honors the injected visibility predicate', () => {
  const doc = setup('<div id="m"><button>a</button><button hidden>b</button><button>c</button></div>');
  const f = lib.focusablesIn(doc.getElementById('m'), VISIBLE);
  assert.deepStrictEqual(f.map(function (el) { return el.textContent; }), ['a', 'c']);
});

test('focusablesIn is empty for a missing or query-less container', () => {
  assert.deepStrictEqual(lib.focusablesIn(null, VISIBLE), []);
  assert.deepStrictEqual(lib.focusablesIn({}, VISIBLE), []);
});

test('Tab on the last element wraps focus to the first', () => {
  const m = trapModal();
  m.last.focus();
  const e = keyEvent('Tab', false);
  assert.strictEqual(lib.handleTrapKeydown(e, m.modal, VISIBLE), true);
  assert.strictEqual(e.prevented, true);
  assert.strictEqual(m.doc.activeElement, m.first);
});

test('Shift+Tab on the first element wraps focus to the last', () => {
  const m = trapModal();
  m.first.focus();
  const e = keyEvent('Tab', true);
  assert.strictEqual(lib.handleTrapKeydown(e, m.modal, VISIBLE), true);
  assert.strictEqual(e.prevented, true);
  assert.strictEqual(m.doc.activeElement, m.last);
});

test('Tab while focus sits outside the dialog pulls it back to the first element', () => {
  const m = trapModal();
  m.outside.focus();
  const e = keyEvent('Tab', false);
  assert.strictEqual(lib.handleTrapKeydown(e, m.modal, VISIBLE), true);
  assert.strictEqual(m.doc.activeElement, m.first);
});

test('Tab in the middle is left to the browser (not trapped)', () => {
  const m = trapModal();
  m.mid.focus();
  const e = keyEvent('Tab', false);
  assert.strictEqual(lib.handleTrapKeydown(e, m.modal, VISIBLE), false);
  assert.strictEqual(e.prevented, false);
  assert.strictEqual(m.doc.activeElement, m.mid);
});

test('Escape (and other non-Tab keys) are not handled by the trap', () => {
  const m = trapModal();
  const e = keyEvent('Escape', false);
  assert.strictEqual(lib.handleTrapKeydown(e, m.modal, VISIBLE), false);
  assert.strictEqual(e.prevented, false);
});

test('Tab in a dialog with no focusables is swallowed', () => {
  const doc = setup('<div id="m"><span>text only</span></div>');
  const e = keyEvent('Tab', false);
  assert.strictEqual(lib.handleTrapKeydown(e, doc.getElementById('m'), VISIBLE), true);
  assert.strictEqual(e.prevented, true);
});

test('handleTrapKeydown is a no-op without a container', () => {
  const e = keyEvent('Tab', false);
  assert.strictEqual(lib.handleTrapKeydown(e, null, VISIBLE), false);
  assert.strictEqual(e.prevented, false);
});

// ---------- status badge rendering ----------

const T = function (k) { return 'T:' + k; }; // i18n stub: echoes the key so we can assert the mapping

test('buildStatusBadge maps an Available request to the available badge', () => {
  const doc = setup('');
  const span = lib.buildStatusBadge(doc, { Status: 3 }, T);
  assert.strictEqual(span.className, 'jellycrowd-status jellycrowd-status-available');
  assert.strictEqual(span.textContent, 'T:status_available');
  assert.strictEqual(span.title, '');
});

test('buildStatusBadge shows "deletion requested" regardless of the underlying status', () => {
  const doc = setup('');
  const span = lib.buildStatusBadge(doc, { Status: 3, DeletionRequestedAt: '2026-06-01T00:00:00Z' }, T);
  assert.strictEqual(span.className, 'jellycrowd-status jellycrowd-status-denied');
  assert.strictEqual(span.textContent, 'T:deletion_requested');
});

test('buildStatusBadge marks a quota-held pending request with a hover hint', () => {
  const doc = setup('');
  const span = lib.buildStatusBadge(doc, { Status: 0, HeldForQuota: true }, T);
  assert.strictEqual(span.className, 'jellycrowd-status jellycrowd-status-held');
  assert.strictEqual(span.textContent, 'T:status_held');
  assert.strictEqual(span.title, 'T:status_held_hint');
});

test('buildStatusBadge leaves a plain pending request untitled', () => {
  const doc = setup('');
  const span = lib.buildStatusBadge(doc, { Status: 0 }, T);
  assert.strictEqual(span.className, 'jellycrowd-status jellycrowd-status-pending');
  assert.strictEqual(span.title, '');
});

// ---------- download badge label (pure) ----------

test('formatBytesDecimal uses decimal (GB=/1000) units', () => {
  assert.strictEqual(lib.formatBytesDecimal(0), '0 B');
  assert.strictEqual(lib.formatBytesDecimal(999), '999 B');
  assert.strictEqual(lib.formatBytesDecimal(1000), '1.0 KB');
  assert.strictEqual(lib.formatBytesDecimal(5470000000), '5.5 GB');
});

test('downloadBadgeLabel composes percent, size and time-left while downloading', () => {
  const label = lib.downloadBadgeLabel(
    { State: 'downloading', Percent: 42, SizeBytes: 5470000000, TimeLeft: '10m' }, T);
  assert.strictEqual(label, 'T:dl_downloading 42% · 5.5 GB · 10m');
});

test('downloadBadgeLabel omits size/time-left when absent', () => {
  assert.strictEqual(lib.downloadBadgeLabel({ State: 'importing', Percent: 100 }, T), 'T:dl_importing 100%');
  assert.strictEqual(lib.downloadBadgeLabel({ State: 'queued' }, T), 'T:dl_queued');
});

test('downloadBadgeLabel appends the release date for an unreleased title', () => {
  // Invalid date falls back to the raw string (deterministic, locale-independent).
  assert.strictEqual(
    lib.downloadBadgeLabel({ State: 'unreleased' }, T, { releaseDate: 'soon' }),
    'T:dl_unreleased · soon');
  // A valid date is localized — assert the stable prefix only.
  assert.ok(lib.downloadBadgeLabel({ State: 'unreleased' }, T, { releaseDate: '2030-01-15' })
    .startsWith('T:dl_unreleased · '));
});

test('downloadBadgeLabel falls back to the raw state for unknown states', () => {
  assert.strictEqual(lib.downloadBadgeLabel({ State: 'weird' }, T), 'weird');
});

// ---------- focus handling when a dialog opens and closes ----------

test('focusFirst moves focus to the first focusable element of the dialog', () => {
  const doc = setup('<button id="outside">out</button>'
    + '<div id="dlg"><span>text</span><button id="a">a</button><button id="b">b</button></div>');
  doc.getElementById('outside').focus();

  const taken = lib.focusFirst(doc.getElementById('dlg'), VISIBLE);

  assert.strictEqual(taken.id, 'a');
  assert.strictEqual(doc.activeElement.id, 'a');
});

test('focusFirst leaves focus alone when the dialog has nothing focusable', () => {
  const doc = setup('<button id="outside">out</button><div id="dlg"><span>just text</span></div>');
  doc.getElementById('outside').focus();

  assert.strictEqual(lib.focusFirst(doc.getElementById('dlg'), VISIBLE), null);
  assert.strictEqual(doc.activeElement.id, 'outside');
});

test('focusRestoreTarget returns the opener so closing hands focus back', () => {
  const doc = setup('<button id="opener">open</button>');
  const opener = doc.getElementById('opener');

  assert.strictEqual(lib.focusRestoreTarget(opener, doc), opener);
});

test('focusRestoreTarget refuses an element that can no longer take focus', () => {
  const doc = setup('<button id="opener">open</button><button id="gone">x</button>');
  const detached = doc.getElementById('gone');
  detached.remove();                                    // re-rendered away while the dialog was open
  const disabled = doc.getElementById('opener');
  disabled.disabled = true;

  assert.strictEqual(lib.focusRestoreTarget(detached, doc), null);
  assert.strictEqual(lib.focusRestoreTarget(disabled, doc), null);
  assert.strictEqual(lib.focusRestoreTarget(null, doc), null);
  assert.strictEqual(lib.focusRestoreTarget({}, doc), null);
});

// ---------- confirmation dialog ----------

const CONFIRM = {
  title: 'Delete media',
  message: 'Delete "Inception" from disk? This is permanent.',
  confirmLabel: 'Delete',
  cancelLabel: 'Cancel',
  danger: true
};

test('buildConfirmDialog announces itself as a modal dialog', () => {
  const doc = setup('');
  const d = lib.buildConfirmDialog(doc, CONFIRM);

  assert.strictEqual(d.root.getAttribute('role'), 'dialog');
  assert.strictEqual(d.root.getAttribute('aria-modal'), 'true');
  assert.strictEqual(d.root.getAttribute('aria-label'), 'Delete media');
  assert.strictEqual(d.title.textContent, 'Delete media');
  assert.strictEqual(d.message.textContent, CONFIRM.message);
  assert.strictEqual(d.confirm.textContent, 'Delete');
  assert.strictEqual(d.cancel.textContent, 'Cancel');
});

test('buildConfirmDialog puts cancel first so a hurried Enter does not delete', () => {
  const doc = setup('');
  const d = lib.buildConfirmDialog(doc, CONFIRM);
  doc.body.appendChild(d.root);

  const order = lib.focusablesIn(d.root, VISIBLE);
  assert.deepStrictEqual(order.map(el => el.className),
    ['jellycrowd-confirm-cancel', 'jellycrowd-confirm-ok jellycrowd-confirm-danger']);
  assert.strictEqual(lib.focusFirst(d.root, VISIBLE), d.cancel);
});

test('buildConfirmDialog marks the confirm button as dangerous only when asked', () => {
  const doc = setup('');
  assert.ok(lib.buildConfirmDialog(doc, CONFIRM).confirm.className.includes('jellycrowd-confirm-danger'));

  const tame = lib.buildConfirmDialog(doc, { title: 'Import', message: 'Import history?', confirmLabel: 'Import' });
  assert.ok(!tame.confirm.className.includes('jellycrowd-confirm-danger'));
});

test('buildConfirmDialog writes text as text, never as markup', () => {
  const doc = setup('');
  const d = lib.buildConfirmDialog(doc, { title: 'x', message: '<img src=x onerror=alert(1)>', confirmLabel: 'ok' });

  assert.strictEqual(d.message.querySelector('img'), null);
  assert.strictEqual(d.message.textContent, '<img src=x onerror=alert(1)>');
});

test('the admin panel asks with its own dialog, not a browser one', () => {
  const fs = require('node:fs');
  const src = fs.readFileSync(
    path.join(__dirname, '..', '..', 'Jellyfin.Plugin.JellyCrowd', 'Web', 'admin.js'), 'utf8')
    .replace(/^\s*\/\/.*$/gm, '');   // drop comments; the ban is on calls, not on mentions
  assert.deepStrictEqual(src.match(/\bwindow\.(confirm|alert|prompt)\s*\(/g) || [], []);
});

// ---------- bulk actions ----------

test('bulkFailureMessage stays quiet when every call went through', () => {
  assert.strictEqual(lib.bulkFailureMessage([true, true, true], T), null);
  assert.strictEqual(lib.bulkFailureMessage([], T), null);
});

test('bulkFailureMessage names how many of how many failed', () => {
  const msg = lib.bulkFailureMessage([true, false, false, true, true], k => '{failed}/{total} ' + k);

  assert.strictEqual(msg, '2/5 bulk_failed');
});

test('bulkFailureMessage treats a whole-batch failure as a failure, not a success', () => {
  assert.strictEqual(lib.bulkFailureMessage([false, false], k => '{failed}/{total} ' + k), '2/2 bulk_failed');
});

// ---------- e-mail sanity check ----------

test('isEmailish accepts the addresses the server would', () => {
  ['a@b.co', 'first.last+tag@sub.example.com', '  spaced@example.org  '].forEach(v => {
    assert.strictEqual(lib.isEmailish(v), true, v);
  });
});

test('isEmailish rejects what is plainly not an address', () => {
  ['', '   ', 'nope', '@example.com', 'a@b', 'a@b.', 'two@at@example.com', 'has space@example.com',
   'x@' + 'y'.repeat(300) + '.com', null, undefined].forEach(v => {
    assert.strictEqual(lib.isEmailish(v), false, String(v));
  });
});

// ---------- loading placeholders ----------

test('buildSkeletons builds card placeholders shaped like a real card', () => {
  const doc = setup('<div id="grid"></div>');
  const grid = doc.getElementById('grid');
  grid.appendChild(lib.buildSkeletons(doc, 'card', 3));

  assert.strictEqual(grid.querySelectorAll('.jellycrowd-skel-card').length, 3);
  assert.strictEqual(grid.querySelectorAll('.jellycrowd-skel-poster').length, 3);
  assert.strictEqual(grid.querySelectorAll('.jellycrowd-skel-line').length, 6); // two lines per card
});

test('buildSkeletons hides placeholders from assistive tech', () => {
  const doc = setup('<div id="grid"></div>');
  doc.getElementById('grid').appendChild(lib.buildSkeletons(doc, 'row', 2));

  const rows = doc.querySelectorAll('.jellycrowd-skel-row');
  assert.strictEqual(rows.length, 2);
  rows.forEach(r => assert.strictEqual(r.getAttribute('aria-hidden'), 'true'));
  assert.strictEqual(doc.querySelectorAll('.jellycrowd-skel-poster').length, 0); // rows have no poster
});

test('buildSkeletons builds nothing for a zero or missing count', () => {
  const doc = setup('<div id="grid"></div>');
  const grid = doc.getElementById('grid');
  grid.appendChild(lib.buildSkeletons(doc, 'card', 0));
  grid.appendChild(lib.buildSkeletons(doc, 'card'));

  assert.strictEqual(grid.childElementCount, 0);
});

test('clearSkeletons removes the placeholders and leaves the real content', () => {
  const doc = setup('<div id="grid"><article class="real">kept</article></div>');
  const grid = doc.getElementById('grid');
  grid.appendChild(lib.buildSkeletons(doc, 'card', 4));

  assert.strictEqual(lib.clearSkeletons(grid), 4);
  assert.strictEqual(grid.querySelectorAll('.jellycrowd-skel').length, 0);
  assert.strictEqual(grid.querySelectorAll('.real').length, 1);
  assert.strictEqual(lib.clearSkeletons(null), 0);
});

// ---------- maskSecret: sensitive settings stay masked until revealed ----------

const SECRET_LABELS = { show: 'Show', hide: 'Hide' };

test('maskSecret turns an input into a password field until the admin reveals it', () => {
  const doc = setup('');
  const input = doc.createElement('input');
  input.type = 'text';
  input.value = 'smtp-password';

  const wrap = lib.maskSecret(input, SECRET_LABELS, doc);
  const button = wrap.querySelector('button');

  assert.strictEqual(input.type, 'password');
  assert.strictEqual(input.getAttribute('autocomplete'), 'new-password'); // no password-manager autofill
  assert.strictEqual(button.textContent, 'Show');
  assert.strictEqual(button.getAttribute('aria-pressed'), 'false');

  button.click();
  assert.strictEqual(input.type, 'text');
  assert.strictEqual(button.textContent, 'Hide');
  assert.strictEqual(button.getAttribute('aria-pressed'), 'true');

  button.click();
  assert.strictEqual(input.type, 'password');
});

test('maskSecret never alters the value, so saving still reads the real secret', () => {
  const doc = setup('');
  const input = doc.createElement('input');
  input.value = 'api-key-123';

  const wrap = lib.maskSecret(input, SECRET_LABELS, doc);
  wrap.querySelector('button').click();
  wrap.querySelector('button').click();

  assert.strictEqual(input.value, 'api-key-123');
  assert.ok(wrap.contains(input)); // the very control the form reads is still in the page
});

test('maskSecret hides a textarea behind a masked stand-in until revealed', () => {
  const doc = setup('');
  const area = doc.createElement('textarea');
  area.value = 'Authorization: Bearer abc123';

  const wrap = lib.maskSecret(area, SECRET_LABELS, doc);
  const standIn = wrap.querySelector('.jellycrowd-secret-standin');

  assert.strictEqual(area.style.display, 'none');
  assert.ok(!standIn.textContent.includes('Bearer'));
  assert.strictEqual(standIn.textContent.length, 8);

  standIn.click(); // clicking the masked value reveals it too
  assert.strictEqual(area.style.display, '');
  assert.strictEqual(standIn.style.display, 'none');
  assert.strictEqual(area.value, 'Authorization: Bearer abc123');
});

test('maskSecret shows an empty stand-in when there is no secret yet', () => {
  const doc = setup('');
  const area = doc.createElement('textarea');

  const wrap = lib.maskSecret(area, SECRET_LABELS, doc);

  assert.strictEqual(wrap.querySelector('.jellycrowd-secret-standin').textContent, '');
});

// ---------- brandLogoSlot: where the branding logo lands in each header layout ----------

// A Jellyfin 12 toolbar as the web client builds it: nav stack (server button + library shortcuts),
// then the box holding the right-hand buttons.
const MUI_TOOLBAR = '<header class="MuiToolbar-root">'
  + '<div class="MuiStack-root">'
  + '<a id="server" href="#/"><img src="icon-transparent.abc.png"></a>'
  + '<a id="movies" href="#/movies"></a>'
  + '</div>'
  + '<div class="MuiBox-root"><button id="search"></button></div>'
  + '</header>';

test('brandLogoSlot puts the logo in the first slot of the Jellyfin 12 nav stack', () => {
  const doc = setup(MUI_TOOLBAR);

  const slot = lib.brandLogoSlot(doc.querySelector('.MuiToolbar-root'));

  assert.strictEqual(slot.parent, doc.querySelector('.MuiStack-root'));
  assert.strictEqual(slot.before, doc.getElementById('server')); // i.e. ahead of the native logo
});

test('brandLogoSlot skips the logo itself, so an already-placed logo is left alone', () => {
  const doc = setup(MUI_TOOLBAR);
  const stack = doc.querySelector('.MuiStack-root');
  const logo = doc.createElement('img');
  stack.insertBefore(logo, stack.firstChild);

  const slot = lib.brandLogoSlot(doc.querySelector('.MuiToolbar-root'), logo);

  assert.strictEqual(slot.parent, stack);
  assert.strictEqual(slot.before, logo.nextSibling); // the caller's idempotence check now matches
});

test('brandLogoSlot falls back to the right-hand box when the toolbar has no nav stack', () => {
  const doc = setup('<header class="MuiToolbar-root"><button id="drawer"></button>'
    + '<div class="MuiBox-root"></div></header>');

  const slot = lib.brandLogoSlot(doc.querySelector('.MuiToolbar-root'));

  assert.strictEqual(slot.parent, doc.querySelector('.MuiToolbar-root'));
  assert.strictEqual(slot.before, doc.querySelector('.MuiBox-root')); // after the drawer button
});

test('brandLogoSlot sits right after the drawer button on the classic 10.11 header', () => {
  const doc = setup('<div class="headerLeft"><button class="mainDrawerButton"></button>'
    + '<div class="pageTitleWithLogo"></div></div>');

  const slot = lib.brandLogoSlot(doc.querySelector('.headerLeft'));

  assert.strictEqual(slot.parent, doc.querySelector('.headerLeft'));
  assert.strictEqual(slot.before, doc.querySelector('.pageTitleWithLogo'));
});

test('brandLogoSlot falls back to the front of a header with no drawer button', () => {
  const doc = setup('<div class="headerLeft"><div id="first"></div></div>');

  const slot = lib.brandLogoSlot(doc.querySelector('.headerLeft'));

  assert.strictEqual(slot.before, doc.getElementById('first'));
});

test('brandLogoSlot has nowhere to place the logo without a header', () => {
  assert.strictEqual(lib.brandLogoSlot(null), null);
});

// ---------- detailPanelAnchor: where our panels hang off the native item-detail page ----------

// 10.11 keeps the sections under the poster/synopsis inside .detailPageContent; Jellyfin 12 dropped
// that wrapper and hangs them off .detailPageSecondaryContainer itself.
const DETAIL_1011 = '<div class="itemDetailPage"><div class="itemBackdrop"></div>'
  + '<div class="detailPagePrimaryContainer"></div>'
  + '<div class="detailPageSecondaryContainer"><div class="detailPageContent"></div></div></div>';
const DETAIL_12 = '<div class="itemDetailPage"><div class="itemBackdrop"></div>'
  + '<div class="detailPageWrapperContainer"><div class="detailPagePrimaryContainer"></div>'
  + '<div class="detailPageSecondaryContainer"></div></div></div>';

test('detailPanelAnchor uses the section wrapper on the 10.11 detail page', () => {
  const doc = setup(DETAIL_1011);

  assert.strictEqual(lib.detailPanelAnchor(doc), doc.querySelector('.detailPageContent'));
});

test('detailPanelAnchor falls back to the secondary container on Jellyfin 12', () => {
  const doc = setup(DETAIL_12);

  const anchor = lib.detailPanelAnchor(doc);

  assert.strictEqual(anchor, doc.querySelector('.detailPageSecondaryContainer'));
  assert.notStrictEqual(anchor, doc.querySelector('.itemDetailPage')); // never the page itself: that is the top of the page
});

test('detailPanelAnchor ignores a hidden detail page left in the DOM', () => {
  const doc = setup('<div class="itemDetailPage hide"><div class="detailPageContent" id="stale"></div></div>'
    + DETAIL_12);

  assert.strictEqual(lib.detailPanelAnchor(doc), doc.querySelector('.detailPageSecondaryContainer'));
});

test('detailPanelAnchor has no anchor while the detail page is still rendering', () => {
  const doc = setup('<div class="itemDetailPage"><div class="itemBackdrop"></div></div>');

  assert.strictEqual(lib.detailPanelAnchor(doc), null); // the caller retries instead of injecting
});

test('detailPanelAnchor is null without a document to search', () => {
  assert.strictEqual(lib.detailPanelAnchor(null), null);
});

// ---------- muiMenuItem: our entry in Jellyfin 12's own user menu ----------

// A native entry as MUI renders it, emotion hashes and all.
const MENU_TEMPLATE = '<ul class="MuiList-root">'
  + '<li class="MuiButtonBase-root MuiMenuItem-root css-abc123" role="menuitem" tabindex="-1">'
  + '<div class="MuiListItemIcon-root css-def456"><svg class="MuiSvgIcon-root"></svg></div>'
  + '<div class="MuiListItemText-root css-ghi789"><span class="MuiTypography-root">Settings</span></div>'
  + '</li></ul>';

test('muiMenuItem copies the native entry classes so it looks like one', () => {
  const doc = setup(MENU_TEMPLATE);

  const item = lib.muiMenuItem(doc.querySelector('.MuiMenuItem-root'), 'Report a problem', 'report_problem');

  assert.strictEqual(item.tagName, 'LI');
  assert.ok(item.className.includes('MuiMenuItem-root'));
  assert.ok(item.className.includes('css-abc123')); // the build-specific hash, read off the live menu
  assert.strictEqual(item.getAttribute('role'), 'menuitem');
  assert.strictEqual(item.getAttribute('tabindex'), '-1');
});

test('muiMenuItem carries our label and icon, not the copied ones', () => {
  const doc = setup(MENU_TEMPLATE);

  const item = lib.muiMenuItem(doc.querySelector('.MuiMenuItem-root'), 'Report a problem', 'report_problem');

  assert.strictEqual(item.querySelector('.MuiTypography-root').textContent, 'Report a problem');
  assert.strictEqual(item.querySelector('.material-icons').textContent, 'report_problem');
  assert.strictEqual(item.querySelector('svg'), null); // the native icon is not cloned along
  assert.ok(item.querySelector('.MuiListItemIcon-root')); // but its wrapper is, for the spacing
});

test('muiMenuItem still builds an entry when the native one has no icon or text wrapper', () => {
  const doc = setup('<ul><li class="MuiMenuItem-root">Sign out</li></ul>');

  const item = lib.muiMenuItem(doc.querySelector('.MuiMenuItem-root'), 'Report a problem', 'report_problem');

  assert.strictEqual(item.textContent, 'report_problemReport a problem');
});

test('muiMenuItem has nothing to copy from without a template', () => {
  assert.strictEqual(lib.muiMenuItem(null, 'Report a problem', 'report_problem'), null);
});
