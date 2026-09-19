/*
 * Jelly Crowd — pure, framework-free helpers shared by catalog.js.
 * UMD wrapper so the same file works as a browser global (JellyCrowdLib)
 * and as a CommonJS module under `node --test` (see tests/js/).
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.JellyCrowdLib = factory();
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // Pick the 2-letter catalog language from a full locale, falling back to 'en'.
  function pickLang(locale, supported) {
    var code = String(locale || 'en').slice(0, 2).toLowerCase();
    return supported.indexOf(code) >= 0 ? code : 'en';
  }

  // Wraps a sensitive form control — a password, API key, token or webhook URL — so its value stays masked
  // until the admin chooses to reveal it: a secret shown in clear ends up in screenshots and over shoulders.
  // An <input> becomes a password field, kept out of the browser's password manager; a <textarea>, which
  // cannot be one, is hidden behind a masked stand-in. The control itself is never replaced or emptied, so
  // saving still reads the real value. `labels` is { show, hide }; `doc` is injectable for tests.
  function maskSecret(control, labels, doc) {
    var d = doc || control.ownerDocument;
    var isInput = control.tagName === 'INPUT';
    var revealed = false;

    var wrap = d.createElement('div');
    wrap.className = 'jellycrowd-secret';
    wrap.style.display = 'flex';
    wrap.style.gap = '0.4em';
    wrap.style.alignItems = 'flex-start';
    control.style.flex = '1';
    control.spellcheck = false;

    var standIn = null;
    if (isInput) {
      control.setAttribute('autocomplete', 'new-password');
    } else {
      standIn = d.createElement('div');
      standIn.className = 'jellycrowd-text-input jellycrowd-secret-standin';
      standIn.style.flex = '1';
      standIn.style.cursor = 'pointer';
      wrap.appendChild(standIn);
    }

    var button = d.createElement('button');
    button.type = 'button';
    button.className = 'jellycrowd-request jellycrowd-secret-toggle';
    button.style.flex = '0 0 auto';

    function render() {
      if (isInput) {
        control.type = revealed ? 'text' : 'password';
      } else {
        control.style.display = revealed ? '' : 'none';
        standIn.style.display = revealed ? 'none' : '';
        standIn.textContent = control.value ? '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022' : '';
      }
      button.textContent = revealed ? labels.hide : labels.show;
      button.setAttribute('aria-pressed', revealed ? 'true' : 'false');
    }

    function toggle() {
      revealed = !revealed;
      render();
      if (revealed && typeof control.focus === 'function') {
        control.focus();
      }
    }

    button.addEventListener('click', toggle);
    if (standIn) {
      standIn.addEventListener('click', toggle);
    }

    wrap.appendChild(control);
    wrap.appendChild(button);
    render();
    return wrap;
  }

  // Fetch an i18n catalog, retrying once before giving up. A single hiccup on this one request used to
  // replace every label of a view with its raw key ("request_button", "report_problem") — silently, with
  // nothing logged, and for as long as the view stayed open, because the loader assigned its empty
  // fallback over the strings already in hand. Resolves to the catalog, or to null so the caller keeps
  // whatever it had rather than degrading to identifiers.
  function fetchStrings(url, fetchImpl) {
    var doFetch = fetchImpl || (typeof fetch === 'function' ? fetch : null);
    if (!doFetch) {
      return Promise.resolve(null);
    }

    function attempt() {
      return Promise.resolve()
        .then(function () { return doFetch(url); })
        .then(function (response) { return response && response.ok ? response.json() : null; })
        .catch(function () { return null; });
    }

    return attempt()
      .then(function (loaded) { return loaded || attempt(); })
      .then(function (loaded) {
        if (!loaded && typeof console !== 'undefined' && console && console.warn) {
          console.warn('Jelly Crowd: could not load ' + url + ' — keeping the labels already loaded.');
        }

        return loaded || null;
      });
  }

  // Resolve the effective 2-letter language: an admin-forced language wins when supported,
  // otherwise 'auto' (or any unsupported value) follows the user's locale.
  function resolveLang(configLang, supported, userLocale) {
    var c = String(configLang || 'auto').toLowerCase();
    if (c !== 'auto' && supported.indexOf(c) >= 0) {
      return c;
    }
    return pickLang(userLocale, supported);
  }

  // Map a 2-letter language to a full TMDB locale; falls back to the user's locale in 'auto' mode.
  function contentLocale(configLang, userLocale) {
    var map = { en: 'en-US', fr: 'fr-FR' };
    var c = String(configLang || 'auto').toLowerCase();
    if (map[c]) {
      return map[c];
    }
    return userLocale || 'en-US';
  }

  // Format a Date (or date-parseable value) as a local 'YYYY-MM-DD' string for <input type="date">.
  function isoDate(value) {
    var d = (value instanceof Date) ? value : new Date(value);
    var month = String(d.getMonth() + 1);
    var day = String(d.getDate());
    return d.getFullYear()
      + '-' + (month.length < 2 ? '0' + month : month)
      + '-' + (day.length < 2 ? '0' + day : day);
  }

  // Extract the 4-digit year from a TMDB date string, or '' when absent.
  function yearOf(item) {
    return item && item.ReleaseDate ? String(item.ReleaseDate).slice(0, 4) : '';
  }

  // "Title (Year)" when a year is known, otherwise just the title.
  function formatTitle(item) {
    var title = (item && item.Title) || '';
    var year = yearOf(item);
    return year ? title + ' (' + year + ')' : title;
  }

  // Map an HTTP error status to the i18n key used for the message.
  function errorKey(status) {
    return status === 503 ? 'error_not_configured' : 'error_generic';
  }

  // Below this many seconds of content after the credits end, there is nothing to skip TO but the next
  // episode (a few seconds of black / a studio logo). Above it, a real bonus remains (a post-credits
  // scene), which the viewer may want to watch — so we offer to jump to it rather than past it.
  var OUTRO_BONUS_MIN_SECONDS = 15;
  var OUTRO_TICKS_PER_SECOND = 10000000;

  // Decide what the Skip Outro control should do for a detected end-credits region. Pure so it can be
  // unit-tested; the DOM/player wiring in header.js consumes the result. Returns null when the region is
  // unusable (no region, or a runtime we don't trust).
  //  - "next":  the credits run to (nearly) the end → advance to the next episode, automatically after a
  //             short countdown. seekSeconds is the end of the file, which ends playback and lets the
  //             client move on.
  //  - "bonus": content remains after the credits → offer to jump to it, and never auto-skip (the viewer
  //             chose to keep watching). seekSeconds is where the credits end.
  function outroSkipPlan(startTicks, endTicks, runTimeTicks) {
    var start = Number(startTicks);
    var end = Number(endTicks);
    var runtime = Number(runTimeTicks);
    if (!(end > start) || !(runtime > 0) || end > runtime) {
      return null;
    }

    var tailSeconds = (runtime - end) / OUTRO_TICKS_PER_SECOND;
    if (tailSeconds <= OUTRO_BONUS_MIN_SECONDS) {
      return { mode: 'next', autoSkipSeconds: 5, seekSeconds: runtime / OUTRO_TICKS_PER_SECOND };
    }

    return { mode: 'bonus', autoSkipSeconds: 0, seekSeconds: end / OUTRO_TICKS_PER_SECOND };
  }

  // Label a season row. The name can be missing (a season the download backend knows about but TMDB does
  // not, e.g. the later seasons of an anime TMDB serves as one), so fall back to a localized "Season N".
  // The episode count is appended only when it is known: null means "not known", never "no episodes".
  function seasonLabel(season, translate) {
    if (!season) {
      return '';
    }

    var name = season.Name || String(translate('season_number')).replace('{n}', String(season.SeasonNumber));
    return season.EpisodeCount ? name + ' (' + season.EpisodeCount + ')' : name;
  }

  // Format a TMDB vote average as a one-decimal string, or '' when there is no rating.
  function formatRating(vote) {
    var n = Number(vote);
    return n > 0 ? n.toFixed(1) : '';
  }

  // Map a request status (numeric enum or string, as serialized by the API) to its i18n key.
  function statusLabelKey(status) {
    var map = {
      '0': 'status_pending', 'pending': 'status_pending',
      '1': 'status_approved', 'approved': 'status_approved',
      '2': 'status_denied', 'denied': 'status_denied',
      '3': 'status_available', 'available': 'status_available'
    };
    return map[String(status).toLowerCase()] || 'status_pending';
  }

  // Status i18n key for a whole request: a pending request held back only by the requester's disk quota
  // (HeldForQuota) reads as "on hold (quota)" rather than the generic "pending (awaiting admin)", since it
  // is not awaiting an admin and resumes automatically once space frees up.
  function requestStatusLabelKey(request) {
    var r = request || {};
    var st = String(r.Status).toLowerCase();
    if ((st === '0' || st === 'pending') && r.HeldForQuota) {
      return 'status_held';
    }
    return statusLabelKey(r.Status);
  }

  // Admin sort order for a request status: Pending, then Approved, then Available, then Denied.
  function statusRank(status) {
    var order = {
      '0': 0, 'pending': 0,
      '1': 1, 'approved': 1,
      '3': 2, 'available': 2,
      '2': 3, 'denied': 3
    };
    var key = String(status).toLowerCase();
    return Object.prototype.hasOwnProperty.call(order, key) ? order[key] : 99;
  }

  // "My requests" autosort tier (lower = higher in the list): Pending, Approved, Downloading,
  // Deletion-requested, Unreleased (future desired date), Available, Denied. `isDownloading` comes from
  // the live Servarr queue. Pure so it can be unit-tested.
  function requestSortRank(request, isDownloading) {
    var r = request || {};
    if (r.DeletionRequestedAt) { return 3; }
    var st = String(r.Status).toLowerCase();
    if (st === '3' || st === 'available') { return 5; }
    if (st === '2' || st === 'denied') { return 6; }
    if (r.DesiredAt && new Date(r.DesiredAt).getTime() > Date.now()) { return 4; } // unreleased / scheduled
    if (isDownloading) { return 2; }
    if (st === '1' || st === 'approved') { return 1; }
    return 0; // pending
  }

  // Return [min, max] from two numbers (used to keep dual-slider bounds ordered).
  function orderPair(a, b) {
    var x = Number(a);
    var y = Number(b);
    return x <= y ? [x, y] : [y, x];
  }

  // Human-readable byte size (binary units).
  function formatBytes(bytes) {
    var n = Number(bytes) || 0;
    var units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    var i = 0;
    while (n >= 1024 && i < units.length - 1) {
      n /= 1024;
      i++;
    }
    return (i === 0 ? n : n.toFixed(1)) + ' ' + units[i];
  }

  // Usage percentage clamped to 0..100; 0 when the quota is unlimited (<= 0).
  function quotaPercent(used, quota) {
    var u = Number(used) || 0;
    var q = Number(quota) || 0;
    if (q <= 0) {
      return 0;
    }
    var p = (u / q) * 100;
    if (p < 0) {
      return 0;
    }
    // Whole number: the bars and labels never want a fractional percent (the dashboard was showing a
    // dozen decimals).
    return Math.round(p > 100 ? 100 : p);
  }

  // Whether a navigation event should close the plugin overlay, given the deadline set when the overlay
  // itself asked the page behind it to go Home. Those navigations are ours and must be ignored; anything
  // after the deadline is the user really leaving. Each ignored event pushes the deadline back, because
  // one navigation can surface as several events (hashchange then popstate) spread over time.
  //
  // A plain "ignore for 200 ms" flag used to lose this race: a home navigation that reported late closed
  // the panel a click had just opened.
  function navCloseAllowed(nowMs, suppressUntilMs) {
    return Number(nowMs) >= (Number(suppressUntilMs) || 0);
  }

  // The deadline after an event we chose to ignore: never earlier than the one we already had.
  function navSuppressWindow(nowMs, suppressUntilMs, extendMs) {
    var extended = Number(nowMs) + (Number(extendMs) || 0);
    var current = Number(suppressUntilMs) || 0;
    return extended > current ? extended : current;
  }

  // The admin report queue, filtered and ordered: 'open' (the default), 'resolved' or 'all'. Open ones
  // always come first, and within each group the OLDEST first — the backlog is what needs attention, and
  // it is exactly what the open-reports reminder describes. Returns a new array; the input is untouched.
  function orderReports(reports, mode) {
    var rows = (reports || []).filter(function (r) {
      if (!r) { return false; }
      if (mode === 'all') { return true; }
      return mode === 'resolved' ? !!r.Resolved : !r.Resolved;
    });
    return rows.sort(function (a, b) {
      if (!a.Resolved !== !b.Resolved) { return a.Resolved ? 1 : -1; }
      return new Date(a.CreatedAt || 0) - new Date(b.CreatedAt || 0);
    });
  }

  // Whether a quota snapshot leaves no room at all: an unlimited quota never does, otherwise it is full
  // once the disk usage reaches it. What a title costs is only known server-side (its size on disk), so
  // this answers the one case the UI can settle on its own — nothing can fit, so actions that consume
  // quota are offered disabled with their reason rather than failing on click.
  function quotaFull(info) {
    if (!info || info.Unlimited) {
      return false;
    }
    var quota = Number(info.QuotaBytes) || 0;
    return quota > 0 && (Number(info.UsedBytes) || 0) >= quota;
  }

  // Whether the library has grown PAST the quota, which is stricter than quotaFull: exactly at the limit
  // is full but not over. Only over does an ownership stop being renewable, so that a library that
  // outgrew its quota shrinks by expiry unless its owner frees something first.
  function quotaOver(info) {
    if (!info || info.Unlimited) {
      return false;
    }
    var quota = Number(info.QuotaBytes) || 0;
    return quota > 0 && (Number(info.UsedBytes) || 0) > quota;
  }

  // Splits the quota bar into what is on disk and what is merely reserved by requests still downloading.
  // The reserved part is drawn after the used part and the two are clamped to 100% together, so a bar
  // that is already full never spills: a user seeing "0 used" while being refused a request needs to see
  // where the space went.
  function quotaSegments(used, reserved, quota) {
    var usedPercent = quotaPercent(used, quota);
    var q = Number(quota) || 0;
    if (q <= 0) {
      return { used: 0, reserved: 0 };
    }

    var r = Number(reserved) || 0;
    var reservedPercent = r > 0 ? Math.round((r / q) * 100) : 0;
    if (reservedPercent < 0) {
      reservedPercent = 0;
    }

    if (usedPercent + reservedPercent > 100) {
      reservedPercent = 100 - usedPercent;
    }

    return { used: usedPercent, reserved: reservedPercent };
  }

  // Group catalog items by their release date (YYYY-MM-DD), returning date-ascending groups.
  // Items without a parseable date are dropped. Used by the releases calendar.
  function groupByReleaseDate(items) {
    var byDate = {};
    var dates = [];
    (items || []).forEach(function (item) {
      var date = item && item.ReleaseDate ? String(item.ReleaseDate).slice(0, 10) : '';
      if (date.length !== 10) {
        return;
      }
      if (!Object.prototype.hasOwnProperty.call(byDate, date)) {
        byDate[date] = [];
        dates.push(date);
      }
      byDate[date].push(item);
    });
    dates.sort();
    return dates.map(function (date) { return { date: date, items: byDate[date] }; });
  }

  // Build a Monday-first month grid as an array of weeks; each week has 7 entries that are either
  // null (padding) or { day: 1..31, iso: 'YYYY-MM-DD' }. `month` is 0-based (0 = January).
  function buildMonthMatrix(year, month) {
    function pad(n) { return n < 10 ? '0' + n : String(n); }
    var startDow = (new Date(Date.UTC(year, month, 1)).getUTCDay() + 6) % 7; // Mon=0 … Sun=6
    var daysInMonth = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
    var weeks = [];
    var week = [];
    var i;
    for (i = 0; i < startDow; i++) { week.push(null); }
    for (var day = 1; day <= daysInMonth; day++) {
      week.push({ day: day, iso: year + '-' + pad(month + 1) + '-' + pad(day) });
      if (week.length === 7) { weeks.push(week); week = []; }
    }
    if (week.length > 0) {
      while (week.length < 7) { week.push(null); }
      weeks.push(week);
    }
    return weeks;
  }

  // Map a live download state (from the Radarr/Sonarr queue) to its i18n key, or '' when unknown.
  function downloadStateKey(state) {
    var map = {
      queued: 'dl_queued',
      downloading: 'dl_downloading',
      importing: 'dl_importing',
      completed: 'dl_completed',
      warning: 'dl_warning',
      missing: 'dl_missing',
      unreleased: 'dl_unreleased'
    };
    return Object.prototype.hasOwnProperty.call(map, String(state).toLowerCase())
      ? map[String(state).toLowerCase()]
      : '';
  }

  // Compact countdown until a target time, e.g. "3d 4h", "5h 10m", "12m". Returns '' when the target
  // is in the past, missing or invalid (caller then shows an "imminent" label).
  function deletionCountdown(targetMs, nowMs) {
    var diff = Number(targetMs) - Number(nowMs);
    if (!isFinite(diff) || diff <= 0) {
      return '';
    }
    var minutes = Math.floor(diff / 60000);
    var days = Math.floor(minutes / 1440);
    minutes -= days * 1440;
    var hours = Math.floor(minutes / 60);
    minutes -= hours * 60;
    if (days > 0) {
      return days + 'd ' + hours + 'h';
    }
    if (hours > 0) {
      return hours + 'h ' + minutes + 'm';
    }
    return minutes + 'm';
  }

  // From the user's requests, the set of seasons/episodes already actively requested for one title.
  // Returns { seasons: {N: true}, episodes: {'N:M': true} }. Denied requests are ignored.
  function requestedKeys(requests, tmdbId) {
    var seasons = {};
    var episodes = {};
    (requests || []).forEach(function (r) {
      if (!r || r.TmdbId !== tmdbId) {
        return;
      }
      if (r.Status === 2 || r.Status === 'Denied' || r.Status === 'denied') {
        return;
      }
      if (r.Season === null || r.Season === undefined) {
        return;
      }
      if (r.Episode === null || r.Episode === undefined) {
        seasons[r.Season] = true;
      } else {
        episodes[r.Season + ':' + r.Episode] = true;
      }
    });
    return { seasons: seasons, episodes: episodes };
  }

  // Build the Jellyfin web details-page hash for a library item, e.g. "#/details?id=ABC&serverId=XYZ".
  // Returns '' when no item id is given. serverId is optional.
  function jellyfinDetailsHash(itemId, serverId) {
    if (!itemId) {
      return '';
    }
    var hash = '#/details?id=' + encodeURIComponent(itemId);
    if (serverId) {
      hash += '&serverId=' + encodeURIComponent(serverId);
    }
    return hash;
  }

  // Quota fill colour, grading from green (empty) through yellow (half) to red (full).
  // Interpolates the HSL hue 120 -> 0 across 0..100%.
  function quotaColor(percent) {
    var p = Number(percent) || 0;
    if (p < 0) { p = 0; }
    if (p > 100) { p = 100; }
    return 'hsl(' + (120 - p * 1.2) + ', 70%, 45%)';
  }

  // Build the branding <style> text from the branding settings (pure, so it's unit-tested). Order
  // matters: any @import (font) must come first to be valid; the admin's free custom CSS comes last so
  // it always wins. Returns '' when branding is disabled. Selectors target Jellyfin's own classes (the
  // same ones used in hand-written custom CSS) and the plugin's own buttons; structural bits (logo,
  // favicon, avatar, drawer links) are applied imperatively by header.js, not here.
  function buildBrandingCss(branding) {
    var b = branding || {};
    if (!b.Enabled) { return ''; }
    var imports = '';
    var rules = [];

    if (b.FontUrl) { imports += '@import url("' + b.FontUrl + '");\n'; }
    if (b.FontFamily) {
      rules.push('body,.ui-body,.page,h1,h2,h3,h4,button,input,select,textarea,.button-link{font-family:' + b.FontFamily + ' !important;}');
    }

    if (b.AccentColor) {
      var a = b.AccentColor;
      rules.push(
        '.button-submit,.raised.button-submit,button.button-submit{background:' + a + ' !important;}'
        + '.mainDrawer .navMenuOption-selected,.emby-tab-button-active{color:' + a + ' !important;}'
        + '.jellycrowd-request:not(.jellycrowd-request-danger):not(.jellycrowd-request-secondary){background:' + a + ';}');
    }

    if (b.BackgroundUrl || b.BackgroundColor) {
      var bg = '.backgroundContainer{';
      if (b.BackgroundColor) { bg += 'background-color:' + b.BackgroundColor + ';'; }
      if (b.BackgroundUrl) { bg += 'background-image:url("' + b.BackgroundUrl + '");background-size:cover;background-position:center;'; }
      bg += '}';
      rules.push(bg);
    }

    // Layout presets — the exact selectors come from real hand-written Jellyfin custom CSS.
    if (b.PresetHideBackdrop) { rules.push('.backdropImage{display:none !important;}'); }
    if (b.PresetDarkIndicators) { rules.push('.indicator{background:#00000058 !important;}.countIndicator{background:#00000058 !important;}'); }
    if (b.PresetNarrowChannels) { rules.push('.channelsContainer{max-width:8em;}'); }
    if (b.PresetButtonTweaks) { rules.push('a.raised.emby-button{padding:0.9em 1em;color:inherit !important;}'); }
    if (b.PresetCompactEpisodes) {
      rules.push('.listItemImage.listItemImage-large.itemAction.lazy{height:110px;}.listItem-content{height:115px;}.secondary.listItem-overview.listItemBodyText{height:61px;margin:0;}');
    }

    if (b.CustomCss) { rules.push(String(b.CustomCss)); }

    return imports + rules.join('\n');
  }

  // ---------- DOM helpers (operate on passed-in elements; still framework-free) ----------

  // The focusable elements inside `container`, in DOM order. `opts.visible(el)` decides visibility
  // (default: the element has layout boxes — correct in a browser, including under a position:fixed
  // overlay where offsetParent would wrongly be null). Tests inject a predicate because jsdom has no
  // layout engine, so getClientRects() is always empty there.
  function focusablesIn(container, opts) {
    if (!container || !container.querySelectorAll) { return []; }
    var isVisible = (opts && opts.visible) || function (el) { return el.getClientRects().length > 0; };
    var sel = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]';
    // tabindex="-1" means "programmatically focusable but out of the tab order" — filter it out for
    // EVERY element type (the :not() on the selector alone wouldn't catch e.g. <input tabindex="-1">,
    // which still matches input:not([disabled])).
    return Array.prototype.slice.call(container.querySelectorAll(sel)).filter(function (el) {
      return el.getAttribute('tabindex') !== '-1' && isVisible(el);
    });
  }

  // Focus trap for a modal dialog: keeps Tab / Shift+Tab cycling within `container` instead of leaking
  // to the page behind it. Call from a keydown handler; returns true when it took over the event (having
  // preventDefault-ed and moved focus). Only Tab is handled — Escape/close is the caller's concern.
  // `opts.doc` overrides the document used for activeElement (defaults to the container's owner
  // document); `opts.visible` is passed through to focusablesIn.
  function handleTrapKeydown(e, container, opts) {
    if (!e || e.key !== 'Tab' || !container) { return false; }
    var doc = (opts && opts.doc) || container.ownerDocument;
    var f = focusablesIn(container, opts);
    if (!f.length) { e.preventDefault(); return true; }
    var first = f[0];
    var last = f[f.length - 1];
    var active = doc ? doc.activeElement : null;
    if (!container.contains(active)) { e.preventDefault(); first.focus(); return true; }
    if (e.shiftKey && active === first) { e.preventDefault(); last.focus(); return true; }
    if (!e.shiftKey && active === last) { e.preventDefault(); first.focus(); return true; }
    return false;
  }

  // Move focus into a dialog when it opens. Without this, a keyboard user who opens the overlay is
  // still focused on the page behind it and has to tab through the whole document to reach it.
  // Returns the element that took focus, or null when there is nothing focusable.
  function focusFirst(container, opts) {
    var f = focusablesIn(container, opts);
    if (!f.length) { return null; }
    f[0].focus();
    return f[0];
  }

  // The element to hand focus back to when a dialog closes: the one that had it when the dialog opened,
  // provided it is still in the document and still focusable. Anything else returns null, so the caller
  // leaves focus alone rather than throwing or focusing a detached node.
  function focusRestoreTarget(saved, doc) {
    if (!saved || typeof saved.focus !== 'function' || saved.disabled) { return null; }
    var d = doc || saved.ownerDocument;
    if (!d || typeof d.contains !== 'function' || !d.contains(saved)) { return null; }
    return saved;
  }

  // Placeholders shaped like the content that is coming, so the page settles into its final layout
  // instead of jumping from a line of text to a full grid. `variant` is 'card' (poster + two lines, for
  // the catalog grid) or 'row' (a list row). They carry .jellycrowd-skel so clearSkeletons can find them
  // again, and aria-hidden because there is nothing here to read out — the region's aria-live announces
  // the real content when it lands.
  function buildSkeletons(doc, variant, count) {
    var frag = doc.createDocumentFragment();
    var n = Math.max(0, count || 0);
    for (var i = 0; i < n; i++) {
      var el = doc.createElement('div');
      el.className = 'jellycrowd-skel jellycrowd-skel-' + (variant === 'row' ? 'row' : 'card');
      el.setAttribute('aria-hidden', 'true');
      if (variant !== 'row') {
        var poster = doc.createElement('div');
        poster.className = 'jellycrowd-skel-poster';
        el.appendChild(poster);
        var line = doc.createElement('div');
        line.className = 'jellycrowd-skel-line';
        el.appendChild(line);
        var short = doc.createElement('div');
        short.className = 'jellycrowd-skel-line jellycrowd-skel-line-short';
        el.appendChild(short);
      }

      frag.appendChild(el);
    }

    return frag;
  }

  // Removes the placeholders from a container, leaving any real content alone. Returns how many went.
  function clearSkeletons(container) {
    if (!container || !container.querySelectorAll) { return 0; }
    var skels = container.querySelectorAll('.jellycrowd-skel');
    for (var i = 0; i < skels.length; i++) {
      if (skels[i].parentNode) { skels[i].parentNode.removeChild(skels[i]); }
    }

    return skels.length;
  }

  // The one-line label for a viewing-history entry: an episode reads "Series · S1E2 · Episode name",
  // a movie is just its title. Pure so it can be unit tested; SxEy is left untranslated (universal).
  function historyEntryLabel(entry) {
    var e = entry || {};
    if (e.SeriesName) {
      var label = e.SeriesName;
      if (e.Season != null && e.Episode != null) {
        label += ' · S' + e.Season + 'E' + e.Episode;
      }

      if (e.Title && e.Title !== e.SeriesName) {
        label += ' · ' + e.Title;
      }

      return label;
    }

    return e.Title || '';
  }

  // Which TMDB discover endpoint a set of filters should hit. A person filter is a "filmography" view,
  // and TMDB only supports person discovery for movies — /discover/tv silently ignores with_people and
  // returns the whole unfiltered catalog — so a person filter always resolves to the movie endpoint,
  // whatever media-type tab the click came from.
  function discoverMediaType(filters) {
    if (filters && filters.personId) { return 'movie'; }
    return (filters && filters.mediaType) || 'movie';
  }

  // A deliberately loose "does this look like an address" check, to catch a typo before a round-trip.
  // The server validates for real before it ever uses the value as an SMTP recipient — this must never
  // be the only gate, and must not reject addresses the server would accept.
  function isEmailish(value) {
    var v = String(value || '').trim();
    if (!v || v.length > 254 || /\s/.test(v)) { return false; }
    var at = v.indexOf('@');
    if (at < 1 || at !== v.lastIndexOf('@')) { return false; }
    var domain = v.slice(at + 1);
    return domain.length > 2 && domain.indexOf('.') > 0 && domain.charAt(domain.length - 1) !== '.';
  }

  // Reports on a batch of independent calls (cancel a whole season, retry a whole season). `results` is
  // one boolean per call. Returns null when every call went through — the refreshed list is feedback
  // enough — and a message when some did not, so that a partial failure cannot pass for a success: the
  // list would simply come back with the untouched rows still in it and no explanation.
  function bulkFailureMessage(results, t) {
    var list = results || [];
    var failed = 0;
    for (var i = 0; i < list.length; i++) {
      if (!list[i]) { failed++; }
    }

    if (!failed) { return null; }
    return t('bulk_failed').replace('{failed}', failed).replace('{total}', list.length);
  }

  // Builds a confirmation dialog and returns its parts. Pure DOM construction: showing it, resolving a
  // choice and handling keys are the caller's job (see confirmAction in admin.js).
  // `opts`: { title, message, confirmLabel, cancelLabel, danger }.
  // Cancel comes first in the DOM on purpose — it is what focusFirst lands on, so the safe answer is
  // the one a hurried Enter picks, which matters when the confirm button deletes files.
  function buildConfirmDialog(doc, opts) {
    var o = opts || {};
    var root = doc.createElement('div');
    root.className = 'jellycrowd-modal-overlay jellycrowd-confirm-overlay';
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    // Labelling by text rather than aria-labelledby avoids minting ids that could collide with the page.
    root.setAttribute('aria-label', o.title || '');

    var card = doc.createElement('div');
    card.className = 'jellycrowd-modal jellycrowd-confirm';

    var titleEl = doc.createElement('h2');
    titleEl.className = 'jellycrowd-confirm-title';
    titleEl.textContent = o.title || '';

    var messageEl = doc.createElement('p');
    messageEl.className = 'jellycrowd-confirm-message';
    messageEl.textContent = o.message || '';

    var actions = doc.createElement('div');
    actions.className = 'jellycrowd-confirm-actions';

    var cancel = doc.createElement('button');
    cancel.type = 'button';
    cancel.className = 'jellycrowd-confirm-cancel';
    cancel.textContent = o.cancelLabel || '';

    var confirm = doc.createElement('button');
    confirm.type = 'button';
    confirm.className = 'jellycrowd-confirm-ok' + (o.danger ? ' jellycrowd-confirm-danger' : '');
    confirm.textContent = o.confirmLabel || '';

    actions.appendChild(cancel);
    actions.appendChild(confirm);
    card.appendChild(titleEl);
    card.appendChild(messageEl);
    card.appendChild(actions);
    root.appendChild(card);

    return { root: root, card: card, title: titleEl, message: messageEl, cancel: cancel, confirm: confirm };
  }

  // Build the primary status badge <span> for a "My requests" row. A pending deletion overrides the
  // status; a quota-held request gets a hover hint. `t` is the i18n lookup; `doc` is the document to
  // create in (pass `document` in the browser; tests pass a jsdom document).
  function buildStatusBadge(doc, request, t) {
    var r = request || {};
    var span = doc.createElement('span');
    if (r.DeletionRequestedAt) {
      span.className = 'jellycrowd-status jellycrowd-status-denied';
      span.textContent = t('deletion_requested');
      return span;
    }
    var key = requestStatusLabelKey(r);
    span.className = 'jellycrowd-status jellycrowd-status-' + key.replace('status_', '');
    span.textContent = t(key);
    if (key === 'status_held') {
      span.title = t('status_held_hint');
    }
    return span;
  }

  // Human-readable byte size in DECIMAL units (GB = /1000), matching what RDT/Radarr report on the
  // download status (the quota bar uses binary GiB via formatBytes). Pure.
  function formatBytesDecimal(bytes) {
    var n = Number(bytes) || 0;
    var units = ['B', 'KB', 'MB', 'GB', 'TB'];
    var i = 0;
    while (n >= 1000 && i < units.length - 1) {
      n /= 1000;
      i++;
    }
    return (i === 0 ? n : n.toFixed(1)) + ' ' + units[i];
  }

  // The text of a live download badge (from the Radarr/Sonarr queue): the localized state label, plus —
  // when downloading/importing — "percent · decimal-size · time-left", or — when unreleased — the
  // release date. `t` is the i18n lookup; `opts.releaseDate` supplies the unreleased date. Pure (DOM-free).
  function downloadBadgeLabel(s, t, opts) {
    var state = (s || {}).State;
    var key = downloadStateKey(state);
    var label = key ? t(key) : String(state);
    if (state === 'downloading' || state === 'importing') {
      label += ' ' + Math.round((s && s.Percent) || 0) + '%';
      if (Number(s && s.SizeBytes) > 0) {
        label += ' · ' + formatBytesDecimal(s.SizeBytes);
      }
      if (s && s.TimeLeft) {
        label += ' · ' + s.TimeLeft;
      }
    } else if (state === 'unreleased' && opts && opts.releaseDate) {
      var rel = new Date(opts.releaseDate);
      label += ' · ' + (isNaN(rel.getTime()) ? String(opts.releaseDate) : rel.toLocaleDateString());
    }
    return label;
  }

  // Build a minimal user-group record from raw admin-form field values, dropping fields that are "not set"
  // so the stored group stays lean (mirrors the per-user override rules). Nullable settings use ''/absent
  // for "not set", 'yes'/'no' for booleans and 'on'/'off' for plugin access; quota is entered in GiB.
  function buildGroupRecord(f, gib) {
    f = f || {};
    var g = {
      Id: f.id,
      Name: (f.name || '').trim(),
      Members: (f.members || []).slice(),
      LibraryIds: (f.libraryIds || []).slice()
    };
    if (f.quotaGib !== '' && f.quotaGib != null && !isNaN(parseFloat(f.quotaGib))) {
      g.QuotaBytes = Math.round(parseFloat(f.quotaGib) * (gib || (1024 * 1024 * 1024)));
    }
    if (f.canRequest === 'yes') { g.CanRequest = true; } else if (f.canRequest === 'no') { g.CanRequest = false; }
    if (f.autoApprove === 'yes') { g.AutoApprove = true; } else if (f.autoApprove === 'no') { g.AutoApprove = false; }
    if (f.maxPerPeriod !== '' && f.maxPerPeriod != null && !isNaN(parseInt(f.maxPerPeriod, 10))) {
      g.MaxRequestsPerPeriod = parseInt(f.maxPerPeriod, 10);
    }
    if (f.pluginAccess === 'on') { g.PluginAccess = true; } else if (f.pluginAccess === 'off') { g.PluginAccess = false; }
    if (f.childMode === true) {
      g.ChildMode = true;
      var age = parseInt(f.childMaxAge, 10);
      g.ChildMaxAge = isNaN(age) ? 0 : age;
    }
    return g;
  }

  // Normalise the request-scope flags from the settings payload: which media types are offered, and which
  // TV request granularities are allowed. Missing/true → enabled; only an explicit false disables. Never
  // lets both media types be off, nor all three granularities off (mirrors the server's safeguard).
  function normalizeRequestScope(cfg) {
    cfg = cfg || {};
    function on(v) { return v !== false; }
    var movies = on(cfg.MoviesEnabled);
    var series = on(cfg.SeriesEnabled);
    if (!movies && !series) { movies = true; series = true; }
    var s = on(cfg.AllowSeriesRequests);
    var se = on(cfg.AllowSeasonRequests);
    var ep = on(cfg.AllowEpisodeRequests);
    if (!s && !se && !ep) { s = true; se = true; ep = true; }
    return { movies: movies, series: series, allowSeries: s, allowSeason: se, allowEpisode: ep };
  }

  // Filter viewing-history entries by a keyword (title / series / library) and an inclusive local date
  // range (from/to as 'YYYY-MM-DD'; empty = unbounded). Pure — used by the Dashboard history sub-tab.
  function filterHistory(entries, opts) {
    entries = entries || [];
    opts = opts || {};
    var q = (opts.query || '').trim().toLowerCase();
    var fromMs = null;
    var toMs = null;
    if (opts.from) { var f = new Date(opts.from + 'T00:00:00'); if (!isNaN(f.getTime())) { fromMs = f.getTime(); } }
    if (opts.to) { var tt = new Date(opts.to + 'T23:59:59.999'); if (!isNaN(tt.getTime())) { toMs = tt.getTime(); } }
    return entries.filter(function (e) {
      if (q) {
        var hay = ((e.Title || '') + ' ' + (e.SeriesName || '') + ' ' + (e.LibraryName || '')).toLowerCase();
        if (hay.indexOf(q) === -1) { return false; }
      }
      if (fromMs !== null || toMs !== null) {
        var when = e.PlayedAtUtc ? new Date(e.PlayedAtUtc).getTime() : NaN;
        if (isNaN(when)) { return false; }
        if (fromMs !== null && when < fromMs) { return false; }
        if (toMs !== null && when > toMs) { return false; }
      }
      return true;
    });
  }

  // The native settings screens the "Settings" menu groups, in order. `route` is the hash fragment that
  // identifies each. Notifications is a Jelly Crowd overlay, appended separately by the tab-bar builders.
  var SETTINGS_TAB_ROUTES = [
    { id: 'profile', route: 'userprofile' },
    { id: 'quickconnect', route: 'quickconnect' },
    { id: 'display', route: 'mypreferencesdisplay' },
    { id: 'home', route: 'mypreferenceshome' },
    { id: 'playback', route: 'mypreferencesplayback' },
    { id: 'subtitles', route: 'mypreferencessubtitles' },
    { id: 'controls', route: 'mypreferencescontrols' }
  ];

  // The settings tab id for a location hash, or null when the hash is not a grouped settings screen.
  function settingsTabIdForHash(hash) {
    var h = (hash || '').toLowerCase();
    for (var i = 0; i < SETTINGS_TAB_ROUTES.length; i++) {
      if (h.indexOf(SETTINGS_TAB_ROUTES[i].route) >= 0) { return SETTINGS_TAB_ROUTES[i].id; }
    }
    return null;
  }

  return {
    normalizeRequestScope: normalizeRequestScope,
    filterHistory: filterHistory,
    settingsTabIdForHash: settingsTabIdForHash,
    SETTINGS_TAB_ROUTES: SETTINGS_TAB_ROUTES,
    buildGroupRecord: buildGroupRecord,
    focusablesIn: focusablesIn,
    handleTrapKeydown: handleTrapKeydown,
    focusFirst: focusFirst,
    focusRestoreTarget: focusRestoreTarget,
    buildConfirmDialog: buildConfirmDialog,
    bulkFailureMessage: bulkFailureMessage,
    discoverMediaType: discoverMediaType,
    historyEntryLabel: historyEntryLabel,
    isEmailish: isEmailish,
    buildSkeletons: buildSkeletons,
    clearSkeletons: clearSkeletons,
    buildStatusBadge: buildStatusBadge,
    formatBytesDecimal: formatBytesDecimal,
    downloadBadgeLabel: downloadBadgeLabel,
    pickLang: pickLang,
    maskSecret: maskSecret,
    fetchStrings: fetchStrings,
    resolveLang: resolveLang,
    contentLocale: contentLocale,
    isoDate: isoDate,
    yearOf: yearOf,
    formatTitle: formatTitle,
    formatRating: formatRating,
    errorKey: errorKey,
    seasonLabel: seasonLabel,
    outroSkipPlan: outroSkipPlan,
    statusLabelKey: statusLabelKey,
    requestStatusLabelKey: requestStatusLabelKey,
    statusRank: statusRank,
    requestSortRank: requestSortRank,
    downloadStateKey: downloadStateKey,
    jellyfinDetailsHash: jellyfinDetailsHash,
    deletionCountdown: deletionCountdown,
    requestedKeys: requestedKeys,
    orderPair: orderPair,
    formatBytes: formatBytes,
    quotaPercent: quotaPercent,
    navCloseAllowed: navCloseAllowed,
    navSuppressWindow: navSuppressWindow,
    orderReports: orderReports,
    quotaFull: quotaFull,
    quotaOver: quotaOver,
    quotaSegments: quotaSegments,
    quotaColor: quotaColor,
    groupByReleaseDate: groupByReleaseDate,
    buildMonthMatrix: buildMonthMatrix,
    buildBrandingCss: buildBrandingCss
  };
});
