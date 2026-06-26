/*
 * Jelly Crowd — web client shell.
 * Injected into the Jellyfin web client (via the File Transformation plugin). Adds Catalog /
 * My Requests entries and a compact quota bar to the top header, and hosts our user pages itself
 * in a full-screen overlay with its own tab bar — so Jelly Crowd no longer depends on the
 * Plugin Pages plugin. Pages render inline (not in an iframe), so window.ApiClient and the active
 * theme are available to them as before. The header DOM is not a public contract, so the selectors
 * below may need tweaking per Jellyfin version.
 */
(function () {
  'use strict';

  var SUPPORTED = ['en', 'fr'];
  var strings = {};
  var cfgLang = 'auto';
  var pluginHidden = false;   // "config mode": hide the plugin from non-admins (decided server-side)

  // The user pages we host. Order defines the overlay tab order.
  var VIEWS = [
    { id: 'catalog', file: 'catalog.html', labelKey: 'nav_catalog' },
    { id: 'calendar', file: 'calendar.html', labelKey: 'nav_calendar' },
    { id: 'requests', file: 'requests.html', labelKey: 'nav_requests' },
    { id: 'mymedia', file: 'mymedia.html', labelKey: 'my_media_title' },
    { id: 'moderation', file: 'moderation.html', labelKey: 'nav_moderation' },
    { id: 'ownership', file: 'ownership.html', labelKey: 'nav_ownership' }
  ];

  // Admin-only nav tabs, in display order. Added once admin status is known (see ensureAdminTabs).
  var ADMIN_TABS = [
    { id: 'moderation', labelKey: 'nav_moderation' },
    { id: 'ownership', labelKey: 'nav_ownership' }
  ];

  var overlay = null;
  var viewHost = null;
  // Per-view refresh callbacks, registered by the hosted pages (see window.jellyCrowdRegisterRefresh).
  // Called every time an already-loaded view is shown again, so e.g. "My requests" picks up a
  // request just made from the catalog without a full page reload.
  var viewRefreshers = {};

  // Header nav buttons (Catalog / My requests) keyed by view id, and which view the overlay is
  // currently showing. The matching button is white (selected); the rest are grey. When the overlay
  // closes, activeNavId is null so all are grey.
  var headerNavButtons = {};
  var activeNavId = null;
  // Set true around a programmatic navigation we trigger ourselves (sending the page behind the overlay
  // to Home on open), so the hashchange listener doesn't mistake it for the user leaving and close us.
  var suppressHashClose = false;
  var bellBadgeEl = null;          // the red unread-count badge on the header bell
  var announcementEls = null;      // { wrap, btn, icon, dot, panel } for the header announcement icon
  var NAV_GREY = 'rgba(255,255,255,0.6)';
  var NAV_WHITE = '#fff';
  var NAV_BLUE = '#00a4dc';

  function setActiveNav(id) {
    activeNavId = id;
    Object.keys(headerNavButtons).forEach(function (key) {
      headerNavButtons[key].style.color = (key === id) ? NAV_WHITE : NAV_GREY;
    });
  }

  function getUrl(p) {
    return (window.ApiClient && window.ApiClient.getUrl) ? window.ApiClient.getUrl(p) : '/' + p;
  }

  function apiAjax(method, path, data) {
    if (window.ApiClient && window.ApiClient.ajax) {
      var opts = { type: method, url: getUrl(path) };
      if (method === 'GET') { opts.dataType = 'json'; }
      if (data !== undefined) {
        opts.data = JSON.stringify(data);
        opts.contentType = 'application/json';
        opts.dataType = 'json';
      }
      return window.ApiClient.ajax(opts);
    }
    return Promise.reject(new Error('no ApiClient'));
  }

  function lang() {
    // Admin-forced language wins when supported; otherwise follow the user's browser language.
    if (cfgLang !== 'auto' && SUPPORTED.indexOf(cfgLang) >= 0) {
      return cfgLang;
    }
    var code = (navigator.language || 'en').slice(0, 2).toLowerCase();
    return SUPPORTED.indexOf(code) >= 0 ? code : 'en';
  }

  function t(key) {
    return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
  }

  var configMode = false;     // raw "config mode" flag (hidden from non-admins)
  var isAdmin = false;        // current user is an administrator (resolved server-side)
  var commentsEnabled = false; // admin opt-in: show internal reviews on the native detail page
  var announcement = { text: '', level: 'green' };
  var discordUrl = '';        // admin opt-in: Discord invite link shown as a header icon ('' = hidden)
  var supportUrl = '';        // admin opt-in: support/donation link shown as a header icon ('' = hidden)

  function loadConfigLang() {
    // Token-free request (works before ApiClient is ready): gives us the language, the raw config-mode
    // flag and the announcement. If config mode is on we hide by default (fail closed) until an
    // authenticated admin check confirms the current user is exempt.
    return fetch(getUrl('JellyCrowd/Settings/Language'))
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) {
        if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); }
        configMode = !!(d && d.Hidden === true);
        pluginHidden = configMode; // fail closed while config mode is on
        commentsEnabled = !!(d && d.CommentsEnabled === true);
        if (d) { announcement = { text: d.AnnouncementText || '', level: d.AnnouncementLevel || 'green' }; }
        discordUrl = (d && d.DiscordInviteUrl) ? String(d.DiscordInviteUrl) : '';
        supportUrl = (d && d.SupportLinkUrl) ? String(d.SupportLinkUrl) : '';
      })
      .catch(function () { /* keep defaults on failure */ });
  }

  // Confirm via the authenticated endpoint whether THIS user is an admin (and, in config mode, exempt).
  // Retried a few times because ApiClient may not be ready at first paint.
  function resolveAdminVisibility(attempt) {
    attempt = attempt || 0;
    apiAjax('GET', 'JellyCrowd/Settings/Visibility')
      .then(function (d) {
        isAdmin = !!(d && d.IsAdmin === true);
        if (configMode && d && d.Visible === true) {
          pluginHidden = false;
        }
        tryInsert();           // ensure elements are present now that visibility/admin is known
        refreshAnnouncement(); // re-render the banner to show the admin edit affordance (once, no loop)
      })
      .catch(function () {
        if (attempt < 5) {
          setTimeout(function () { resolveAdminVisibility(attempt + 1); }, 1000);
        }
      });
  }

  // Config mode hides the plugin from everyone except administrators.
  function pluginVisible() {
    return !pluginHidden;
  }

  function loadStrings() {
    return fetch(getUrl('JellyCrowd/Web/strings/' + lang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .then(function (d) { strings = d || {}; })
      .catch(function () { strings = {}; });
  }

  // ---------- overlay app ----------

  // Re-run <script> tags found in an injected fragment: nodes added via innerHTML do not execute.
  function executeScripts(container) {
    var scripts = container.querySelectorAll('script');
    for (var i = 0; i < scripts.length; i++) {
      var old = scripts[i];
      var fresh = document.createElement('script');
      for (var a = 0; a < old.attributes.length; a++) {
        fresh.setAttribute(old.attributes[a].name, old.attributes[a].value);
      }
      if (!old.src) {
        fresh.textContent = old.textContent;
      }
      old.parentNode.replaceChild(fresh, old);
    }
  }

  // Fill the overlay user badge with the current Jellyfin user's avatar + name.
  function populateUserBadge(el) {
    if (!(window.ApiClient && typeof window.ApiClient.getCurrentUser === 'function')) {
      return;
    }
    window.ApiClient.getCurrentUser().then(function (user) {
      if (!user) {
        return;
      }
      if (user.PrimaryImageTag && typeof window.ApiClient.getUserImageUrl === 'function') {
        var img = document.createElement('img');
        img.className = 'jellycrowd-overlay-avatar';
        img.alt = '';
        try {
          img.src = window.ApiClient.getUserImageUrl(user.Id, { type: 'Primary', tag: user.PrimaryImageTag });
        } catch (e) { /* no image */ }
        img.addEventListener('error', function () { img.remove(); });
        el.appendChild(img);
      }
      var name = document.createElement('span');
      name.className = 'jellycrowd-overlay-username';
      name.textContent = user.Name || '';
      el.appendChild(name);
    }).catch(function () { /* badge is best-effort */ });
  }

  // Position the overlay just below the native Jellyfin header so the real (custom-CSS) header stays
  // visible and usable. We no longer draw our own header bar — navigation lives in the native header
  // (the injected Catalog / Calendar / My requests links + the quota bar + the bell).
  function positionOverlay() {
    if (!overlay) {
      return;
    }
    var header = document.querySelector('.skinHeader');
    var top = header ? Math.round(header.getBoundingClientRect().bottom) : 0;
    overlay.style.top = (top > 0 ? top : 0) + 'px';
  }

  function ensureOverlay() {
    if (overlay) {
      return;
    }
    overlay = document.createElement('div');
    overlay.className = 'jellycrowd-overlay';
    overlay.style.display = 'none';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', t('app_title'));

    viewHost = document.createElement('div');
    viewHost.className = 'jellycrowd-overlay-views';
    overlay.appendChild(viewHost);

    // A visible close button on the panel itself (the native header sits above; the panel is below it).
    var close = document.createElement('button');
    close.type = 'button';
    close.setAttribute('aria-label', t('back'));
    close.textContent = '×';
    close.style.cssText = 'position:absolute;top:.5em;right:.7em;z-index:5;width:2.1em;height:2.1em;border:0;border-radius:50%;background:rgba(0,0,0,.55);color:#fff;font-size:1.4em;line-height:1;cursor:pointer;display:flex;align-items:center;justify-content:center;';
    close.addEventListener('mouseenter', function () { close.style.background = 'rgba(0,0,0,.8)'; });
    close.addEventListener('mouseleave', function () { close.style.background = 'rgba(0,0,0,.55)'; });
    close.addEventListener('click', hideOverlay);
    overlay.appendChild(close);

    document.body.appendChild(overlay);

    window.addEventListener('resize', positionOverlay);
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && overlay.style.display !== 'none') {
        hideOverlay();
      }
    });
  }

  function showView(id) {
    ensureOverlay();
    var view = null;
    VIEWS.forEach(function (v) { if (v.id === id) { view = v; } });
    if (!view) {
      return;
    }

    // On a fresh open (overlay was closed), send the background page to Home so closing returns there.
    if (overlay.style.display === 'none') {
      sendBackgroundHome();
    }

    overlay.style.display = '';
    // Lock the page behind the overlay so it doesn't scroll under it (phantom scroll on mobile, where
    // the native header also hides on scroll). Restored in hideOverlay().
    document.body.classList.add('jellycrowd-overlay-open');
    positionOverlay();
    setActiveNav(id);
    refreshQuota();      // M28: usage/notifs reflect any change since last view, without a force-refresh
    refreshBellBadge();
    VIEWS.forEach(function (v) {
      if (v.container) {
        v.container.style.display = (v.id === id) ? '' : 'none';
      }
    });

    if (view.container) {
      // Already loaded; just shown above. Let the page refresh its data if it registered a handler.
      if (typeof viewRefreshers[id] === 'function') {
        viewRefreshers[id]();
      }
      return;
    }

    var container = document.createElement('div');
    container.className = 'jellycrowd-view';
    view.container = container;
    viewHost.appendChild(container);

    fetch(getUrl('JellyCrowd/Web/' + view.file))
      .then(function (r) { return r.ok ? r.text() : ''; })
      .then(function (html) {
        container.innerHTML = html;
        executeScripts(container);
      })
      .catch(function () { container.textContent = t('error_generic'); });
  }

  function hideOverlay() {
    if (overlay) {
      overlay.style.display = 'none';
    }
    document.body.classList.remove('jellycrowd-overlay-open');
    setActiveNav(null);
  }

  // Clicking the already-active header link again closes the panel (so the native header alone remains).
  function toggleView(id) {
    if (!pluginVisible()) {
      return;
    }
    if (overlay && overlay.style.display !== 'none' && activeNavId === id) {
      hideOverlay();
    } else {
      showView(id);
    }
  }

  // Let our pages (e.g. the requests quota bar) switch views without touching the URL hash.
  window.jellyCrowdShowView = showView;

  // Let a hosted page register a callback re-run each time its (already-loaded) view is shown again.
  window.jellyCrowdRegisterRefresh = function (id, fn) { viewRefreshers[id] = fn; };

  // Shared media-detail modal: the catalog view owns openModal; other views (e.g. My requests) call
  // window.jellyCrowdOpenDetail(item) to open it. If the catalog view isn't loaded yet, we load it
  // hidden (which registers the opener) and flush the pending item.
  var detailOpener = null;
  var pendingDetailItem = null;

  function ensureViewLoaded(id) {
    ensureOverlay();
    var view = null;
    VIEWS.forEach(function (v) { if (v.id === id) { view = v; } });
    if (!view || view.container) { return; }
    var container = document.createElement('div');
    container.className = 'jellycrowd-view';
    container.style.display = 'none';
    view.container = container;
    viewHost.appendChild(container);
    fetch(getUrl('JellyCrowd/Web/' + view.file))
      .then(function (r) { return r.ok ? r.text() : ''; })
      .then(function (html) { container.innerHTML = html; executeScripts(container); })
      .catch(function () { /* ignore */ });
  }

  window.jellyCrowdRegisterDetailOpener = function (fn) {
    detailOpener = fn;
    if (pendingDetailItem) {
      var it = pendingDetailItem;
      pendingDetailItem = null;
      try { fn(it); } catch (e) { /* ignore */ }
    }
  };

  window.jellyCrowdOpenDetail = function (item) {
    if (detailOpener) { detailOpener(item); return; }
    pendingDetailItem = item;
    ensureViewLoaded('catalog'); // loads catalog.js → registers the opener → flushes pendingDetailItem
  };

  // ---------- header injection ----------

  function navButton(labelKey, viewId) {
    var a = document.createElement('button');
    a.type = 'button';
    a.className = 'jcHeaderTab';
    a.textContent = t(labelKey);
    // Replicate Jellyfin's .emby-tab-button look INLINE (jellycrowd.css isn't loaded on the base page):
    // same font / weight / padding so we sit flush with Home / Favorites. border:0 + outline:none drop
    // the default <button> outline that the native is="emby-button" tabs don't have. We deliberately do
    // NOT reuse the .emby-tab-button class: inside Jellyfin's emby-tabs, that made our buttons get
    // treated as real tabs (Jellyfin would navigate on click, closing the overlay / blanking the page).
    a.style.cssText = 'box-sizing:border-box;margin:0;padding:1.5em 1.5em;border:0;outline:none;box-shadow:none;background:transparent;font-family:inherit;font-size:0.92em;font-weight:600;line-height:1.25;cursor:pointer;white-space:nowrap;';
    headerNavButtons[viewId] = a;
    a.style.color = (viewId === activeNavId) ? NAV_WHITE : NAV_GREY;
    // Hover turns blue (Jellyfin accent); on leave restore the selected/unselected colour.
    a.addEventListener('mouseenter', function () { a.style.color = NAV_BLUE; });
    a.addEventListener('mouseleave', function () { a.style.color = (viewId === activeNavId) ? NAV_WHITE : NAV_GREY; });
    // stopPropagation: keep the click from reaching Jellyfin's tab-bar click handler.
    a.addEventListener('click', function (e) { e.stopPropagation(); toggleView(viewId); });
    return a;
  }

  // Navigate to the Jellyfin home page and close our overlay. Prefer the native home button (proper
  // SPA navigation across versions); fall back to the home route.
  function goHome() {
    hideOverlay();
    var hb = document.querySelector('.headerHomeButton');
    if (hb) { hb.click(); } else { window.location.hash = '#/home.html'; }
  }

  function isOnHome() {
    var h = (window.location.hash || '').toLowerCase();
    return h === '' || h === '#' || h === '#/' || h === '#!/' || h.indexOf('home') >= 0;
  }

  // N33: send the page *behind* the overlay to Home (without closing the overlay), so that whenever the
  // user closes the panel — however they close it — they land back on Home, not some deep page.
  function sendBackgroundHome() {
    if (isOnHome()) {
      return; // already home: nothing to navigate, and no hashchange would fire to clear the flag.
    }
    suppressHashClose = true; // the resulting nav event(s) are ours — ignored by the close listener.
    var hb = document.querySelector('.headerHomeButton');
    if (hb) { hb.click(); } else { window.location.hash = '#/home.html'; }
    // Clear shortly after so the whole navigation burst (hashchange and/or popstate) is covered, then
    // normal "navigation closes the overlay" behaviour resumes.
    setTimeout(function () { suppressHashClose = false; }, 200);
  }

  // A "Home" link styled like our other nav tabs but acting as a real Home navigation (and closing
  // the plugin overlay). Shown first, on Home and in every library.
  function homeButton() {
    var a = document.createElement('button');
    a.type = 'button';
    a.className = 'jcHeaderTab';
    a.textContent = t('nav_home');
    a.style.cssText = 'box-sizing:border-box;margin:0;padding:1.5em 1.5em;border:0;outline:none;box-shadow:none;background:transparent;font-family:inherit;font-size:0.92em;font-weight:600;line-height:1.25;cursor:pointer;white-space:nowrap;';
    a.style.color = NAV_GREY;
    a.addEventListener('mouseenter', function () { a.style.color = NAV_BLUE; });
    a.addEventListener('mouseleave', function () { a.style.color = NAV_GREY; });
    a.addEventListener('click', function (e) { e.stopPropagation(); goHome(); });
    return a;
  }

  // Quota fill colour, grading green (empty) -> yellow (half) -> red (full). Mirrors
  // JellyCrowdLib.quotaColor, duplicated because the base-page header has no access to that module.
  function quotaColor(percent) {
    var p = Number(percent) || 0;
    if (p < 0) { p = 0; }
    if (p > 100) { p = 100; }
    return 'hsl(' + (120 - p * 1.2) + ', 70%, 45%)';
  }

  function bytes(n) {
    n = Number(n) || 0;
    var u = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    var i = 0;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return (i === 0 ? n : n.toFixed(1)) + ' ' + u[i];
  }

  var quotaBox = null; // the current header quota element, so it can be refreshed in place (M28)

  // Re-fetch the quota and update the bar in place (no rebuild) — called after any create/cancel/claim/
  // delete and on view changes so usage reflects without a force-refresh.
  function refreshQuota() {
    var box = quotaBox;
    if (!box || !box._jc || !(window.ApiClient && window.ApiClient.ajax)) { return; }
    var els = box._jc;
    window.ApiClient.ajax({ type: 'GET', url: getUrl('JellyCrowd/Quota/Me'), dataType: 'json' })
      .then(function (q) {
        if (!q) { return; }
        els.tier.style.display = 'none';
        box.title = t('my_media_title');
        if (q.Unlimited || q.QuotaBytes <= 0) {
          els.label.textContent = t('quota_storage') + ': ' + bytes(q.UsedBytes) + ' / ' + t('quota_unlimited');
          els.track.style.display = 'none';
        } else {
          els.label.textContent = bytes(q.UsedBytes) + ' / ' + bytes(q.QuotaBytes);
          els.track.style.display = '';
          var p = q.QuotaBytes > 0 ? Math.min(100, q.UsedBytes / q.QuotaBytes * 100) : 0;
          els.fill.style.width = p + '%';
          els.fill.style.background = quotaColor(p);
        }
        if (q.AdaptiveEnabled) {
          if (q.InProbation) {
            var days = q.ProbationEndsUtc ? Math.max(0, Math.ceil((new Date(q.ProbationEndsUtc) - new Date()) / 86400000)) : 0;
            els.tier.textContent = '⏳ ' + t('quota_probation').replace('{n}', days);
            els.tier.style.color = '#ffb300';
            els.tier.style.display = '';
            box.title = t('quota_probation_hint');
          } else if (q.Tier === 'ceiling') {
            els.tier.textContent = '★ ' + t('quota_tier_ceiling');
            els.tier.style.color = '#4caf50';
            els.tier.style.display = '';
          } else if (q.Tier === 'floor') {
            els.tier.textContent = '▼ ' + t('quota_tier_floor');
            els.tier.style.color = '#ff7043';
            els.tier.style.display = '';
          }
        }
      })
      .catch(function () { /* ignore */ });
  }

  function buildQuota() {
    var box = document.createElement('span');
    box.style.cssText = 'display:inline-flex;flex-direction:column;justify-content:center;min-width:8em;margin:0 .6em;font-size:.7em;cursor:pointer;line-height:1.05;';
    box.title = t('my_media_title');
    // Keyboard-accessible (it's a clickable span acting as a button).
    box.setAttribute('role', 'button');
    box.setAttribute('tabindex', '0');
    box.setAttribute('aria-label', t('my_media_title'));
    box.addEventListener('click', function () { toggleView('mymedia'); });
    box.addEventListener('keydown', function (e) {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleView('mymedia'); }
    });
    var caption = document.createElement('span');
    caption.textContent = t('my_media_title');
    caption.style.cssText = 'color:#4caf50;font-weight:700;font-size:1.25em;line-height:1.1;white-space:nowrap;transition:filter .1s;';
    // Hover affordance: it's clickable (opens My library), so brighten + underline the caption.
    box.addEventListener('mouseenter', function () { caption.style.filter = 'brightness(1.25)'; caption.style.textDecoration = 'underline'; });
    box.addEventListener('mouseleave', function () { caption.style.filter = ''; caption.style.textDecoration = ''; });
    var label = document.createElement('span');
    label.style.color = '#fff';
    var tier = document.createElement('span');
    tier.style.cssText = 'font-weight:700;white-space:nowrap;display:none;';
    var track = document.createElement('span');
    track.style.cssText = 'height:.35em;border-radius:.2em;background:rgba(255,255,255,.2);overflow:hidden;display:block;margin-top:.2em;';
    var fill = document.createElement('span');
    fill.style.cssText = 'display:block;height:100%;background:' + quotaColor(0) + ';width:0%;';
    track.appendChild(fill);
    box.appendChild(caption);
    box.appendChild(label);
    box.appendChild(tier);
    box.appendChild(track);

    box._jc = { label: label, tier: tier, track: track, fill: fill };
    quotaBox = box;
    refreshQuota();
    return box;
  }

  // Let the hosted views refresh the header quota bar after a mutating action (M28 reactivity).
  window.jellyCrowdRefreshQuota = refreshQuota;

  // Catalog / My requests render as extra tabs right next to Jellyfin's own Home / Favorites, inside
  // the centered .headerTabs row. That row is page-specific (shown on Home / library pages, hidden on
  // detail / search / settings), so these links follow the same visibility — by design. Jellyfin
  // rebuilds the tab bar on navigation, so the MutationObserver re-inserts us whenever it's wiped.
  // The admin-only tabs are added last and only once admin status is known. It can resolve after the
  // first insertNav, so this runs again from tryInsert() to top up an already-built nav.
  function ensureAdminTabs(nav) {
    if (!isAdmin) {
      return;
    }
    ADMIN_TABS.forEach(function (tab) {
      if (!headerNavButtons[tab.id]) {
        nav.appendChild(navButton(tab.labelKey, tab.id));
      }
    });
  }

  function insertNav() {
    var existingNav = document.querySelector('.jcHeaderNav');
    if (existingNav) {
      ensureAdminTabs(existingNav); // admin status may have resolved since the first insert
      return;
    }
    // Prefer the native tabs row (centered). Fall back to the header's left area for library types
    // (Other/Books) that don't render a tabs row, so our nav is still reachable there.
    var tabs = document.querySelector('.headerTabs.sectionTabs') || document.querySelector('.headerTabs');
    var host = tabs || document.querySelector('.skinHeader .headerLeft') || document.querySelector('.headerLeft');
    if (!host) {
      return;
    }
    var nav = document.createElement('div');
    nav.className = 'jcHeaderNav';
    nav.style.cssText = 'display:inline-flex;align-items:center;';
    nav.appendChild(homeButton());
    nav.appendChild(navButton('nav_catalog', 'catalog'));
    nav.appendChild(navButton('nav_calendar', 'calendar'));
    nav.appendChild(navButton('nav_requests', 'requests'));
    ensureAdminTabs(nav);
    // Sit on the same line as the real tabs when the slider exists, else in the row/host itself.
    var slider = tabs ? tabs.querySelector('.emby-tabs-slider') : null;
    (slider || host).appendChild(nav);
  }

  // Brand SVGs for the optional header links. Inline (the base page can't load external assets, and a
  // strict CSP would block remote images anyway). `currentColor` so they inherit the header text colour.
  var DISCORD_SVG = '<svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" aria-hidden="true"><path d="M20.317 4.369A19.79 19.79 0 0 0 15.885 3c-.21.375-.45.88-.617 1.28a18.27 18.27 0 0 0-5.535 0A12.6 12.6 0 0 0 9.11 3 19.7 19.7 0 0 0 4.677 4.37C1.99 8.38 1.26 12.29 1.62 16.14a19.9 19.9 0 0 0 6.07 3.06c.49-.67.93-1.38 1.3-2.13-.71-.27-1.39-.6-2.03-.99.17-.13.34-.26.5-.4 3.93 1.84 8.18 1.84 12.06 0 .16.14.33.27.5.4-.65.39-1.33.72-2.04.99.37.75.81 1.46 1.3 2.13a19.84 19.84 0 0 0 6.07-3.06c.42-4.46-.73-8.34-3.05-11.77ZM8.52 13.79c-1.18 0-2.15-1.08-2.15-2.41 0-1.33.95-2.42 2.15-2.42 1.21 0 2.18 1.09 2.16 2.42 0 1.33-.95 2.41-2.16 2.41Zm6.96 0c-1.18 0-2.15-1.08-2.15-2.41 0-1.33.95-2.42 2.15-2.42 1.21 0 2.18 1.09 2.16 2.42 0 1.33-.95 2.41-2.16 2.41Z"/></svg>';
  var SUPPORT_SVG = '<svg viewBox="0 0 16 16" width="23" height="23" fill="currentColor" aria-hidden="true"><path d="M5.5 9.511c.076.954.83 1.697 2.182 1.785V12h.6v-.709c1.4-.098 2.218-.846 2.218-1.932 0-.987-.626-1.496-1.745-1.76l-.473-.112V5.57c.6.068.982.396 1.074.85h1.052c-.076-.919-.864-1.638-2.126-1.716V4h-.6v.719c-1.195.117-2.01.836-2.01 1.853 0 .9.606 1.472 1.613 1.707l.397.098v2.034c-.615-.093-1.022-.43-1.114-.9H5.5zm2.177-2.166c-.59-.137-.91-.416-.91-.836 0-.47.345-.822.915-.925v1.76h-.005zm.692 1.193c.717.166 1.048.435 1.048.91 0 .542-.412.914-1.135.982V8.518l.087.02z"/><path d="M8 13.5a5.5 5.5 0 1 1 0-11 5.5 5.5 0 0 1 0 11zm0 .5A6 6 0 1 0 8 2a6 6 0 0 0 0 12z"/></svg>';

  function brandLinkIcon(svg, href, title, cls) {
    var a = document.createElement('a');
    // Native icon-button classes so these match the right-side header buttons (bell) in size/shape;
    // green (the quota accent) and a larger glyph, per request.
    a.className = 'paper-icon-button-light headerButton jcHeaderLink ' + cls;
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.title = title;
    a.setAttribute('aria-label', title);
    a.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;color:#4caf50;text-decoration:none;';
    a.innerHTML = svg;
    a.addEventListener('mouseenter', function () { a.style.filter = 'brightness(1.2)'; });
    a.addEventListener('mouseleave', function () { a.style.filter = ''; });
    a.addEventListener('click', function (e) { e.stopPropagation(); });
    return a;
  }

  // Optional admin-configured links, on the LEFT immediately after the brand logo / home button
  // (Discord, then Support). Idempotent (admin config can resolve after the first insert) and gated on
  // a configured URL.
  function insertHeaderLinks() {
    if (!discordUrl && !supportUrl) {
      return;
    }
    var left = document.querySelector('.skinHeader .headerLeft') || document.querySelector('.headerLeft');
    if (!left) {
      return;
    }
    var group = left.querySelector('.jcHeaderLinks');
    if (!group) {
      group = document.createElement('span');
      group.className = 'jcHeaderLinks';
      group.style.cssText = 'display:inline-flex;align-items:center;align-self:center;';
      // Sit right after the logo / home button so the icons hug the brand on the left.
      var anchor = left.querySelector('.pageTitleWithLogo') || left.querySelector('.headerHomeButton') || left.querySelector('.pageTitle');
      if (anchor) { anchor.insertAdjacentElement('afterend', group); } else { left.appendChild(group); }
    }
    if (discordUrl && !group.querySelector('.jcHeaderLink-discord')) {
      group.appendChild(brandLinkIcon(DISCORD_SVG, discordUrl, t('discord_link_title'), 'jcHeaderLink-discord'));
    }
    if (supportUrl && !group.querySelector('.jcHeaderLink-support')) {
      group.appendChild(brandLinkIcon(SUPPORT_SVG, supportUrl, t('support_link_title'), 'jcHeaderLink-support'));
    }
  }

  // Quota bar lives in .headerRight, placed between the search icon and the user avatar
  // (i.e. just before the .headerUserButton), per request.
  function insertQuota() {
    var host = document.querySelector('.headerRight');
    if (!host || document.querySelector('.jcHeaderQuota')) {
      return;
    }
    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderQuota';
    // align-self:center so the block is centred against the native header row regardless of its
    // default alignment; the column block is otherwise taller than its neighbours and sits low.
    wrap.style.cssText = 'display:inline-flex;align-items:center;align-self:center;height:100%;';
    wrap.appendChild(buildQuota());
    var userBtn = host.querySelector('.headerUserButton');
    if (userBtn) {
      host.insertBefore(wrap, userBtn);
    } else {
      host.appendChild(wrap);
    }
  }

  // ---------- notification bell ----------

  function setBellBadge(count) {
    if (!bellBadgeEl) {
      return;
    }
    if (count > 0) {
      bellBadgeEl.textContent = count > 99 ? '99+' : String(count);
      bellBadgeEl.style.display = '';
    } else {
      bellBadgeEl.style.display = 'none';
    }
  }

  function refreshBellBadge() {
    if (!bellBadgeEl) {
      return;
    }
    apiAjax('GET', 'JellyCrowd/Notifications/Mine')
      .then(function (d) { setBellBadge(d ? d.Unread : 0); })
      .catch(function () { /* best-effort */ });
  }

  // A status emoji per notification (green ✅ good / red 🟥 bad / yellow 🟨 warning).
  function notifEmoji(event) {
    switch (event) {
      case 'Approved':
      case 'Available':
      case 'AvailableReleased':
      case 'AvailableUnreleased':
        return '✅';
      case 'Denied':
      case 'Failed':
        return '🟥';
      case 'QuotaExpiry':
        return '🟨';
      default:
        return '🔔';
    }
  }

  function renderBellList(panel, items) {
    panel.innerHTML = '';
    var head = document.createElement('div');
    head.style.cssText = 'display:flex;justify-content:space-between;align-items:center;padding:.5em .7em;border-bottom:1px solid rgba(255,255,255,.12);position:sticky;top:0;background:#1c1c1c;';
    var title = document.createElement('span');
    title.textContent = t('notifications');
    title.style.fontWeight = '600';
    head.appendChild(title);

    var actions = document.createElement('span');
    actions.style.cssText = 'display:inline-flex;gap:.6em;align-items:center;';
    if (items && items.length) {
      var clear = document.createElement('button');
      clear.type = 'button';
      clear.textContent = t('notif_clear_all');
      clear.style.cssText = 'background:none;border:0;color:#00a4dc;cursor:pointer;font-size:.85em;';
      clear.addEventListener('click', function () {
        apiAjax('POST', 'JellyCrowd/Notifications/Mine/Clear')
          .then(function () { renderBellList(panel, []); setBellBadge(0); })
          .catch(function () { /* ignore */ });
      });
      actions.appendChild(clear);
    }
    var gear = document.createElement('button');
    gear.type = 'button';
    gear.title = t('notif_settings');
    gear.textContent = '⚙';
    gear.style.cssText = 'background:none;border:0;color:#fff;cursor:pointer;font-size:1em;opacity:.8;';
    gear.addEventListener('click', function () { renderPrefsForm(panel); });
    actions.appendChild(gear);
    head.appendChild(actions);
    panel.appendChild(head);

    if (!items || !items.length) {
      var empty = document.createElement('div');
      empty.style.cssText = 'padding:1em .7em;opacity:.7;';
      empty.textContent = t('notif_empty');
      panel.appendChild(empty);
      return;
    }

    items.forEach(function (n) {
      var row = document.createElement('div');
      row.style.cssText = 'display:flex;align-items:flex-start;gap:.4em;padding:.55em .7em;border-bottom:1px solid rgba(255,255,255,.07);' + (n.Read ? '' : 'background:rgba(0,164,220,.08);');
      var icon = document.createElement('span');
      icon.textContent = notifEmoji(n.Event);
      icon.style.cssText = 'flex:0 0 auto;font-size:.95em;line-height:1.3;';
      row.appendChild(icon);
      var content = document.createElement('div');
      content.style.cssText = 'flex:1;min-width:0;';
      var line1 = document.createElement('div');
      line1.textContent = n.Title;
      line1.style.cssText = 'font-weight:600;font-size:.9em;';
      var line2 = document.createElement('div');
      line2.textContent = n.Message;
      line2.style.cssText = 'font-size:.82em;opacity:.85;margin-top:.1em;';
      var line3 = document.createElement('div');
      line3.textContent = n.CreatedAt ? new Date(n.CreatedAt).toLocaleString() : '';
      line3.style.cssText = 'font-size:.72em;opacity:.55;margin-top:.15em;';
      content.appendChild(line1);
      content.appendChild(line2);
      content.appendChild(line3);
      row.appendChild(content);

      // Per-item clear (×) — removes just this notification.
      var del = document.createElement('button');
      del.type = 'button';
      del.title = t('notif_clear_one');
      del.textContent = '×';
      del.style.cssText = 'background:none;border:0;color:#fff;opacity:.5;cursor:pointer;font-size:1.1em;line-height:1;flex:0 0 auto;';
      del.addEventListener('click', function () {
        del.disabled = true;
        apiAjax('POST', 'JellyCrowd/Notifications/Mine/Clear/' + n.Id)
          .then(function () {
            row.remove();
            if (!panel.querySelector('[data-jc-notif-row]')) { renderBellList(panel, []); }
          })
          .catch(function () { del.disabled = false; });
      });
      row.setAttribute('data-jc-notif-row', '1');
      row.appendChild(del);
      panel.appendChild(row);
    });
  }

  function renderPrefsForm(panel) {
    panel.innerHTML = '<div style="padding:.8em;opacity:.7;">' + t('loading') + '</div>';
    apiAjax('GET', 'JellyCrowd/Notifications/Mine/Prefs')
      .then(function (p) {
        p = p || {};
        panel.innerHTML = '';
        var head = document.createElement('div');
        head.style.cssText = 'display:flex;justify-content:space-between;align-items:center;padding:.5em .7em;border-bottom:1px solid rgba(255,255,255,.12);';
        var back = document.createElement('button');
        back.type = 'button';
        back.textContent = '←';
        back.style.cssText = 'background:none;border:0;color:#fff;cursor:pointer;font-size:1em;';
        back.addEventListener('click', function () { openBellPanel(panel); });
        var title = document.createElement('span');
        title.textContent = t('notif_settings');
        title.style.fontWeight = '600';
        head.appendChild(back);
        head.appendChild(title);
        head.appendChild(document.createElement('span'));
        panel.appendChild(head);

        var form = document.createElement('div');
        form.style.cssText = 'padding:.7em;display:flex;flex-direction:column;gap:.6em;';

        var enaLabel = document.createElement('label');
        enaLabel.style.cssText = 'display:flex;align-items:center;gap:.5em;cursor:pointer;';
        var ena = document.createElement('input');
        ena.type = 'checkbox';
        ena.checked = p.Enabled !== false;
        enaLabel.appendChild(ena);
        var enaText = document.createElement('span');
        enaText.textContent = t('notif_enabled');
        enaLabel.appendChild(enaText);
        form.appendChild(enaLabel);

        function field(labelKey, value, placeholder) {
          var wrap = document.createElement('label');
          wrap.style.cssText = 'display:flex;flex-direction:column;gap:.2em;font-size:.85em;';
          var lab = document.createElement('span');
          lab.textContent = t(labelKey);
          var inp = document.createElement('input');
          inp.type = 'text';
          inp.value = value || '';
          inp.placeholder = placeholder || '';
          inp.style.cssText = 'padding:.35em .5em;border-radius:.25em;border:1px solid rgba(255,255,255,.25);background:#000;color:#fff;';
          wrap.appendChild(lab);
          wrap.appendChild(inp);
          form.appendChild(wrap);
          return inp;
        }

        var emailInp = field('notif_email', p.Email, 'you@example.com');
        var ntfyInp = field('notif_ntfy_topic', p.NtfyTopic, 'my-topic');

        // Per-category opt-ins for personal (email / ntfy) delivery. The in-app bell stays always-on.
        var catHead = document.createElement('div');
        catHead.textContent = t('notif_categories');
        catHead.style.cssText = 'margin-top:.3em;font-size:.78em;opacity:.7;';
        form.appendChild(catHead);

        function toggle(labelKey, checked) {
          var lab = document.createElement('label');
          lab.style.cssText = 'display:flex;align-items:flex-start;gap:.5em;cursor:pointer;font-size:.85em;';
          var cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.checked = !!checked;
          var span = document.createElement('span');
          span.textContent = t(labelKey);
          lab.appendChild(cb);
          lab.appendChild(span);
          form.appendChild(lab);
          return cb;
        }

        var unrel = toggle('notif_cat_unreleased', p.NotifyAvailableUnreleased);
        var rel = toggle('notif_cat_released', p.NotifyAvailableReleased);
        var dec = toggle('notif_cat_decisions', p.NotifyDecisions);
        var quo = toggle('notif_cat_quota', p.NotifyQuotaExpiry);

        var save = document.createElement('button');
        save.type = 'button';
        save.textContent = t('save');
        save.style.cssText = 'align-self:flex-start;background:#00a4dc;border:0;color:#fff;padding:.4em .9em;border-radius:.25em;cursor:pointer;';
        save.addEventListener('click', function () {
          save.disabled = true;
          save.textContent = '…';
          apiAjax('POST', 'JellyCrowd/Notifications/Mine/Prefs', {
            Enabled: ena.checked,
            Email: emailInp.value.trim(),
            NtfyTopic: ntfyInp.value.trim(),
            NotifyAvailableUnreleased: unrel.checked,
            NotifyAvailableReleased: rel.checked,
            NotifyDecisions: dec.checked,
            NotifyQuotaExpiry: quo.checked
          })
            .then(function () { save.textContent = t('saved'); setTimeout(function () { openBellPanel(panel); }, 700); })
            .catch(function () { save.disabled = false; save.textContent = t('save'); });
        });
        form.appendChild(save);
        panel.appendChild(form);
      })
      .catch(function () { panel.innerHTML = '<div style="padding:.8em;">' + t('error_generic') + '</div>'; });
  }

  function openBellPanel(panel) {
    panel.style.display = 'block';
    panel.innerHTML = '<div style="padding:.8em;opacity:.7;">' + t('loading') + '</div>';
    apiAjax('GET', 'JellyCrowd/Notifications/Mine')
      .then(function (d) {
        renderBellList(panel, d ? d.Items : []);
        // Opening the panel counts as seeing them.
        apiAjax('POST', 'JellyCrowd/Notifications/Mine/Read').then(function () { setBellBadge(0); }).catch(function () { /* ignore */ });
      })
      .catch(function () { panel.innerHTML = '<div style="padding:.8em;">' + t('error_generic') + '</div>'; });
  }

  // Bell sits in .headerRight, just left of the quota bar.
  function insertBell() {
    var host = document.querySelector('.headerRight');
    if (!host || document.querySelector('.jcHeaderBell')) {
      return;
    }
    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderBell';
    // align-self:center so the bell is vertically centred in the header bar like the nav tabs (it sat
    // slightly low otherwise, not inheriting the row's centering).
    wrap.style.cssText = 'position:relative;display:inline-flex;align-items:center;align-self:center;';

    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'paper-icon-button-light headerButton';
    btn.title = t('notifications');
    // overflow:visible so the round icon button doesn't clip the corner badge.
    btn.style.cssText = 'position:relative;overflow:visible;';
    var icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = 'notifications';
    btn.appendChild(icon);

    var badge = document.createElement('span');
    badge.className = 'jcBellBadge';
    // Sit at the outer top-right corner (fully outside the icon glyph) so it is never half-hidden.
    badge.style.cssText = 'position:absolute;top:-.15em;right:-.15em;min-width:1.2em;height:1.2em;padding:0 .25em;border-radius:.6em;background:#e53935;color:#fff;font-size:.62em;line-height:1.2em;text-align:center;display:none;box-sizing:border-box;z-index:1;pointer-events:none;';
    btn.appendChild(badge);

    // The panel is fixed-position and lives on <body> (not inside the header) so it is never clipped
    // by the header's overflow/stacking context; we anchor it under the bell button on open.
    var panel = document.createElement('div');
    panel.className = 'jcBellPanel';
    panel.style.cssText = 'position:fixed;width:22em;max-width:90vw;max-height:70vh;overflow-y:auto;background:#1c1c1c;border:1px solid rgba(255,255,255,.15);border-radius:.4em;box-shadow:0 6px 22px rgba(0,0,0,.55);z-index:100000;display:none;';

    function positionPanel() {
      var r = btn.getBoundingClientRect();
      panel.style.top = Math.round(r.bottom + 4) + 'px';
      if (window.innerWidth <= 600) {
        // On phones, span (almost) full width so the panel never lands off-screen to the left.
        panel.style.left = '0.5em';
        panel.style.right = '0.5em';
        panel.style.width = 'auto';
      } else {
        panel.style.left = 'auto';
        panel.style.width = '22em';
        panel.style.right = Math.round(window.innerWidth - r.right) + 'px';
      }
    }

    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      if (panel.style.display !== 'none') { panel.style.display = 'none'; return; }
      positionPanel();
      openBellPanel(panel);
    });
    panel.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () { panel.style.display = 'none'; });

    wrap.appendChild(btn);
    document.body.appendChild(panel);

    var quota = host.querySelector('.jcHeaderQuota');
    var userBtn = host.querySelector('.headerUserButton');
    host.insertBefore(wrap, quota || userBtn || null);

    bellBadgeEl = badge;
    refreshBellBadge();
  }

  // ---------- admin announcement banner ----------

  function announcementColors(level) {
    if (level === 'red') { return { bg: '#c62828', fg: '#fff' }; }
    if (level === 'yellow') { return { bg: '#f9a825', fg: '#1a1a1a' }; }
    return { bg: '#2e7d32', fg: '#fff' }; // green
  }

  // Inline emphasis: strikethrough (~~), bold (**), underline (__), italic (* or _). Runs on
  // already-escaped text and never sees link URLs (those are pulled out first), so it can't mangle them.
  function mdEmphasis(s) {
    s = s.replace(/~~(.+?)~~/g, '<s>$1</s>');
    s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
    s = s.replace(/__(.+?)__/g, '<u>$1</u>');
    s = s.replace(/\*(.+?)\*/g, '<em>$1</em>');
    s = s.replace(/_(.+?)_/g, '<em>$1</em>');
    return s;
  }

  // Minimal, safe markdown for the admin announcement: HTML-escape first, then [text](url) links, bold
  // (**), underline (__), italic (* or _), strikethrough (~~), bullet lists (- / *), and line breaks.
  // The author is the admin (trusted), but we escape anyway to avoid accidental HTML injection, and only
  // allow http(s)/mailto URLs so a [x](javascript:…) link can't run script.
  function mdInline(s) {
    s = s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    // Pull links out to placeholders so the emphasis pass below can't corrupt a URL (e.g. a/b_c_d).
    var links = [];
    s = s.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, function (m, label, url) {
      if (!/^(https?:\/\/|mailto:)/i.test(url)) { return m; }
      var href = url.replace(/"/g, '%22');
      links.push('<a href="' + href + '" target="_blank" rel="noopener noreferrer">' + mdEmphasis(label) + '</a>');
      return '@@JCLINK' + (links.length - 1) + '@@';
    });
    s = mdEmphasis(s);
    return s.replace(/@@JCLINK(\d+)@@/g, function (m, i) { return links[Number(i)]; });
  }

  function renderAnnouncementMarkdown(text) {
    var segments = [];
    var listItems = null;
    String(text || '').split(/\r?\n/).forEach(function (line) {
      var bullet = line.match(/^\s*[-*]\s+(.*)$/);
      if (bullet) {
        listItems = listItems || [];
        listItems.push('<li>' + mdInline(bullet[1]) + '</li>');
        return;
      }
      if (listItems) { segments.push('<ul>' + listItems.join('') + '</ul>'); listItems = null; }
      segments.push(mdInline(line));
    });
    if (listItems) { segments.push('<ul>' + listItems.join('') + '</ul>'); }
    // Join with <br>, but don't add breaks directly around list blocks.
    return segments.join('<br>').replace(/<br>(<ul>)/g, '$1').replace(/(<\/ul>)<br>/g, '$1');
  }

  // The announcement the local user has already opened (so a new one shows a red dot until viewed).
  function lastSeenAnnouncement() {
    try { return window.localStorage.getItem('jcCrowdAnnouncementSeen') || ''; } catch (e) { return ''; }
  }

  function markAnnouncementSeen() {
    try { window.localStorage.setItem('jcCrowdAnnouncementSeen', (announcement.text || '').trim()); } catch (e) { /* ignore */ }
  }

  // Fill the announcement popover: a level-coloured header (+ admin edit/clear) and the markdown body.
  function renderAnnouncementPanel(panel) {
    panel.innerHTML = '';
    var hasText = !!(announcement.text && announcement.text.trim());
    var c = announcementColors(announcement.level);

    var head = document.createElement('div');
    head.style.cssText = 'display:flex;align-items:center;justify-content:space-between;gap:.5em;padding:.55em .8em;font-weight:700;'
      + (hasText ? ('background:' + c.bg + ';color:' + c.fg + ';') : 'border-bottom:1px solid rgba(255,255,255,.12);');
    var title = document.createElement('span');
    title.textContent = t('announcement_title');
    head.appendChild(title);
    if (isAdmin) {
      var edit = document.createElement('button');
      edit.type = 'button';
      edit.textContent = hasText ? '✎' : t('announcement_add');
      edit.title = t('announcement_edit');
      edit.style.cssText = 'background:none;border:0;color:inherit;cursor:pointer;font-size:1em;flex:0 0 auto;';
      edit.addEventListener('click', function (e) { e.stopPropagation(); panel.style.display = 'none'; openAnnouncementEditor(); });
      head.appendChild(edit);
    }
    panel.appendChild(head);

    var body = document.createElement('div');
    body.className = 'jcAnnounceText';
    body.style.cssText = 'padding:.7em .8em;line-height:1.45;word-break:break-word;';
    if (hasText) {
      body.innerHTML = renderAnnouncementMarkdown(announcement.text);
    } else {
      body.style.opacity = '.7';
      body.textContent = isAdmin ? t('announcement_placeholder') : '';
    }
    panel.appendChild(body);
  }

  // Refresh the header icon: visibility, level tint, and the red "new" dot; keep the panel content fresh.
  function updateAnnouncementUi() {
    if (!announcementEls) { return; }
    var els = announcementEls;
    var hasText = !!(announcement.text && announcement.text.trim());
    els.wrap.style.display = (!hasText && !isAdmin) ? 'none' : 'inline-flex';
    els.icon.style.color = hasText ? announcementColors(announcement.level).bg : '';
    var isNew = hasText && (announcement.text.trim() !== lastSeenAnnouncement());
    els.dot.style.display = isNew ? '' : 'none';
    renderAnnouncementPanel(els.panel);
  }

  function openAnnouncementEditor() {
    if (document.getElementById('jcAnnEditor')) { return; }
    var pop = document.createElement('div');
    pop.id = 'jcAnnEditor';
    pop.style.cssText = 'position:fixed;z-index:100001;top:3.4em;left:1em;width:24em;max-width:92vw;background:#1c1c1c;border:1px solid rgba(255,255,255,.18);border-radius:.4em;box-shadow:0 8px 26px rgba(0,0,0,.55);padding:.7em;color:#fff;';
    var ta = document.createElement('textarea');
    ta.value = announcement.text || '';
    ta.placeholder = t('announcement_placeholder');
    ta.style.cssText = 'width:100%;min-height:3em;box-sizing:border-box;background:#111;color:#fff;border:1px solid rgba(255,255,255,.2);border-radius:.3em;padding:.4em;';
    var sel = document.createElement('select');
    [['green', t('announcement_green')], ['yellow', t('announcement_yellow')], ['red', t('announcement_red')]].forEach(function (o) {
      var op = document.createElement('option'); op.value = o[0]; op.textContent = o[1];
      op.style.backgroundColor = '#1c1c1c'; op.style.color = '#fff';
      sel.appendChild(op);
    });
    sel.value = announcement.level || 'green';
    sel.style.cssText = 'margin-top:.5em;background:#111;color:#fff;border:1px solid rgba(255,255,255,.2);border-radius:.3em;padding:.3em;';
    var actions = document.createElement('div');
    actions.style.cssText = 'display:flex;gap:.5em;margin-top:.6em;justify-content:flex-end;';
    // The endpoint returns 204 No Content, so we must NOT ask ApiClient to parse JSON (it would
    // reject on the empty body and the banner would only update on the next page load).
    function save(text, level, btn) {
      if (!(window.ApiClient && window.ApiClient.ajax)) { return; }
      saveBtn.disabled = true; clearBtn.disabled = true;
      var prev = btn ? btn.textContent : '';
      if (btn) { btn.textContent = '…'; }
      window.ApiClient.ajax({
        type: 'POST',
        url: getUrl('JellyCrowd/Settings/Announcement'),
        data: JSON.stringify({ Text: text, Level: level }),
        contentType: 'application/json'
      })
        .then(function () {
          announcement = { text: (text || '').trim(), level: level };
          refreshAnnouncement();           // live update of the banner — the visible success signal
          if (btn) { btn.textContent = '✓'; }
          setTimeout(function () { pop.remove(); }, 500);
        })
        .catch(function () {
          saveBtn.disabled = false; clearBtn.disabled = false;
          if (btn) { btn.textContent = prev; }
          ta.style.border = '1px solid #c62828';
        });
    }
    var clearBtn = document.createElement('button');
    clearBtn.type = 'button'; clearBtn.textContent = t('announcement_clear');
    clearBtn.style.cssText = 'background:none;border:1px solid rgba(255,255,255,.3);color:#fff;border-radius:.3em;padding:.3em .7em;cursor:pointer;';
    clearBtn.addEventListener('click', function () { save('', sel.value, clearBtn); });
    var saveBtn = document.createElement('button');
    saveBtn.type = 'button'; saveBtn.textContent = t('save');
    saveBtn.style.cssText = 'background:#00a4dc;border:0;color:#fff;border-radius:.3em;padding:.3em .8em;cursor:pointer;';
    saveBtn.addEventListener('click', function () { save(ta.value, sel.value, saveBtn); });
    actions.appendChild(clearBtn); actions.appendChild(saveBtn);
    pop.appendChild(ta); pop.appendChild(sel); pop.appendChild(actions);
    pop.addEventListener('click', function (e) { e.stopPropagation(); });
    document.body.appendChild(pop);
    var onDoc = function () { pop.remove(); document.removeEventListener('click', onDoc); };
    setTimeout(function () { document.addEventListener('click', onDoc); }, 0);
  }

  function refreshAnnouncement() {
    updateAnnouncementUi();
  }

  // The announcement is a header ICON with a popover (like the bell): an inline multi-line banner was
  // cramped and clipped in the header. A red dot marks a new (unseen) announcement; the icon is tinted by
  // level. Built once; content refreshes via refreshAnnouncement().
  function insertAnnouncement() {
    var host = document.querySelector('.headerRight');
    if (!host || document.querySelector('.jcHeaderAnnounce')) { return; }

    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderAnnounce';
    wrap.style.cssText = 'position:relative;display:inline-flex;align-items:center;align-self:center;';

    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'paper-icon-button-light headerButton';
    btn.title = t('announcement_title');
    btn.style.cssText = 'position:relative;overflow:visible;';
    var icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = 'campaign';
    btn.appendChild(icon);

    var dot = document.createElement('span');
    dot.className = 'jcAnnounceDot';
    dot.style.cssText = 'position:absolute;top:-.05em;right:-.05em;width:.7em;height:.7em;border-radius:50%;background:#e53935;display:none;box-sizing:border-box;z-index:1;pointer-events:none;';
    btn.appendChild(dot);

    // Wider than the bell panel so announcements display nicely; fixed-position on <body> so it's never
    // clipped by the header's overflow/stacking context.
    var panel = document.createElement('div');
    panel.className = 'jcAnnouncePanel';
    panel.style.cssText = 'position:fixed;width:28em;max-width:92vw;max-height:70vh;overflow-y:auto;background:#1c1c1c;border:1px solid rgba(255,255,255,.15);border-radius:.4em;box-shadow:0 6px 22px rgba(0,0,0,.55);z-index:100000;display:none;';

    function positionPanel() {
      var r = btn.getBoundingClientRect();
      panel.style.top = Math.round(r.bottom + 4) + 'px';
      if (window.innerWidth <= 600) {
        panel.style.left = '0.5em';
        panel.style.right = '0.5em';
        panel.style.width = 'auto';
      } else {
        panel.style.left = 'auto';
        panel.style.width = '28em';
        panel.style.right = Math.round(window.innerWidth - r.right) + 'px';
      }
    }

    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      if (panel.style.display !== 'none') { panel.style.display = 'none'; return; }
      renderAnnouncementPanel(panel);
      positionPanel();
      panel.style.display = '';
      markAnnouncementSeen();
      dot.style.display = 'none';
    });
    panel.addEventListener('click', function (e) { e.stopPropagation(); });
    document.addEventListener('click', function () { panel.style.display = 'none'; });

    wrap.appendChild(btn);
    document.body.appendChild(panel);
    // Sit just left of the bell, within the header's icon cluster.
    var bell = host.querySelector('.jcHeaderBell');
    host.insertBefore(wrap, bell || host.querySelector('.jcHeaderQuota') || host.querySelector('.headerUserButton') || null);

    announcementEls = { wrap: wrap, btn: btn, icon: icon, dot: dot, panel: panel };
    updateAnnouncementUi();
  }

  // Hide Jellyfin's native section tabs (Home/Favorites, Movies/Suggestions/…, Shows/…): Jelly Crowd
  // supplies its own nav in that row instead. Injected once; harmless if the row isn't present.
  function injectHeaderStyle() {
    if (document.getElementById('jcHeaderStyle')) {
      return;
    }
    var style = document.createElement('style');
    style.id = 'jcHeaderStyle';
    style.textContent =
      '.headerTabs .emby-tab-button{display:none !important;}' +
      // Some library types (Other/Books) hide the empty tab row — keep it shown when it hosts our nav.
      '.headerTabs:has(.jcHeaderNav){display:flex !important;justify-content:center;}' +
      // The header logo / home button is a link to Home — show it as one (pointer cursor on hover).
      '.skinHeader .headerHomeButton,.skinHeader .pageTitleWithLogo,.skinHeader .pageTitle{cursor:pointer;}' +
      // The nav tabs row sits a few px higher than the rest of the bar; nudge our injected elements
      // (announcement, bell, quota) up to line up with it.
      '.skinHeader .jcHeaderAnnounce,.skinHeader .jcHeaderBell,.skinHeader .jcHeaderQuota{position:relative;top:-4px;}' +
      // Compact bullet lists inside the (markdown) announcement.
      '.jcAnnounceText ul{margin:.15em 0;padding-left:1.1em;}.jcAnnounceText li{margin:0;}';
    document.head.appendChild(style);
  }

  // In "config mode" the plugin is hidden from non-admins — in that case we must NOT hide the native
  // tabs, otherwise those users get an empty header (our nav isn't inserted to fill it).
  function removeHeaderStyle() {
    var style = document.getElementById('jcHeaderStyle');
    if (style && style.parentNode) { style.parentNode.removeChild(style); }
  }

  function tryInsert() {
    if (!pluginVisible()) {
      removeHeaderStyle(); // restore the native header tabs for users who can't see the plugin
      return;
    }
    injectHeaderStyle();
    insertNav();
    insertHeaderLinks();
    insertQuota();
    insertBell();
    insertAnnouncement();
  }

  // ---------- Internal reviews on the native Jellyfin detail page (M25.2) ----------
  // Admin opt-in (CommentsEnabled). The native detail DOM is not a contract, so this is defensive:
  // it fails silently when it can't find an anchor or resolve a TMDB id, and never loops.
  var detailReviewsLoadedId = null;   // item id we've rendered reviews for
  var detailReviewsPendingId = null;  // item id whose reviews are being fetched

  function currentDetailItemId() {
    var h = window.location.hash || '';
    var m = h.match(/[?&]id=([a-f0-9]{32})/i);
    return m ? m[1] : null;
  }

  function removeDetailReviews() {
    var el = document.getElementById('jcDetailReviews');
    if (el && el.parentNode) { el.parentNode.removeChild(el); }
    detailReviewsLoadedId = null;
    detailReviewsPendingId = null;
  }

  // A compact gold/grey star row for a 1–10 rating (each star = 2 points; rounded to nearest star).
  function reviewStarRow(value10) {
    var span = document.createElement('span');
    var full = Math.round(value10 / 2);
    if (full < 0) { full = 0; }
    if (full > 5) { full = 5; }
    span.textContent = '★★★★★'.slice(0, full) + '☆☆☆☆☆'.slice(0, 5 - full);
    span.style.cssText = 'color:#f5c518;letter-spacing:1px;';
    return span;
  }

  // Interactive 1–10 star input (5 stars, half-star granularity) with hover preview — same UX as the
  // catalog popup. getValue() returns the committed 1–10 value (0 = unrated).
  function reviewStarInput(initial) {
    var wrap = document.createElement('span');
    wrap.style.cssText = 'display:inline-flex;gap:.1em;font-size:1.7em;line-height:1;cursor:pointer;vertical-align:middle;';
    var value = initial || 0;
    var fills = [];
    function render(shown) {
      var v = (shown === undefined || shown === null) ? value : shown;
      fills.forEach(function (f, idx) { f.style.width = Math.max(0, Math.min(100, (v - idx * 2) / 2 * 100)) + '%'; });
    }
    function valueAt(star, idx, clientX) {
      var r = star.getBoundingClientRect();
      return idx * 2 + ((clientX - r.left) < r.width / 2 ? 1 : 2);
    }
    for (var i = 0; i < 5; i++) {
      (function (idx) {
        var star = document.createElement('span');
        star.style.cssText = 'position:relative;display:inline-block;width:1em;color:#888;';
        star.textContent = '★';
        var fill = document.createElement('span');
        fill.style.cssText = 'position:absolute;left:0;top:0;overflow:hidden;white-space:nowrap;color:#f5c518;width:0;';
        fill.textContent = '★';
        star.appendChild(fill);
        star.addEventListener('mousemove', function (e) { render(valueAt(star, idx, e.clientX)); });
        star.addEventListener('click', function (e) { value = valueAt(star, idx, e.clientX); render(); });
        fills.push(fill);
        wrap.appendChild(star);
      })(i);
    }
    wrap.addEventListener('mouseleave', function () { render(); });
    render();
    wrap.getValue = function () { return value; };
    return wrap;
  }

  function buildDetailReviewsPanel(item, dto) {
    dto = dto || { Average: 0, Count: 0, Reviews: [] };
    var panel = document.createElement('div');
    panel.id = 'jcDetailReviews';
    panel.style.cssText = 'margin:1.5em 0;padding:1em 1.2em;border-radius:.5em;background:rgba(255,255,255,.05);max-width:900px;';

    var title = document.createElement('h2');
    title.textContent = t('reviews');
    title.style.cssText = 'margin:0 0 .5em;font-size:1.2em;';
    panel.appendChild(title);

    var avg = document.createElement('div');
    avg.style.cssText = 'display:flex;align-items:center;gap:.5em;margin-bottom:.8em;';
    if (dto.Count > 0) {
      avg.appendChild(reviewStarRow(dto.Average));
      var num = document.createElement('span');
      num.textContent = dto.Average.toFixed(1) + '/10 · ' + dto.Count + ' ' + t('ratings_count');
      num.style.opacity = '.8';
      avg.appendChild(num);
    } else {
      var none = document.createElement('span');
      none.textContent = t('reviews_none');
      none.style.opacity = '.7';
      avg.appendChild(none);
    }
    panel.appendChild(avg);

    // Your review: star rating (1–10, like the popup) + optional multiline text, laid out vertically.
    var form = document.createElement('div');
    form.style.cssText = 'display:flex;flex-direction:column;align-items:flex-start;gap:.5em;margin-bottom:1em;';
    var mine = (dto.Reviews || []).filter(function (r) { return r.Mine; })[0];

    var rateRow = document.createElement('div');
    rateRow.style.cssText = 'display:flex;align-items:center;gap:.5em;';
    var rateLabel = document.createElement('span');
    rateLabel.textContent = t('your_review') + ' :';
    var stars = reviewStarInput(mine && mine.Rating ? mine.Rating : 0);
    rateRow.appendChild(rateLabel);
    rateRow.appendChild(stars);
    form.appendChild(rateRow);

    var text = document.createElement('textarea');
    text.rows = 3;
    text.placeholder = t('review_text_placeholder');
    text.value = mine && mine.Text ? mine.Text : '';
    text.style.cssText = 'width:100%;max-width:520px;box-sizing:border-box;min-height:4em;resize:vertical;padding:.5em .6em;border-radius:.25em;border:1px solid rgba(255,255,255,.25);background:#000;color:#fff;font-family:inherit;';
    form.appendChild(text);

    var post = document.createElement('button');
    post.type = 'button';
    post.textContent = mine ? t('review_update') : t('review_submit');
    post.style.cssText = 'background:#00a4dc;border:0;color:#fff;padding:.45em 1em;border-radius:.25em;cursor:pointer;';
    post.addEventListener('click', function () {
      var rating = stars.getValue();
      if (rating < 1) { post.textContent = t('rating_required'); setTimeout(function () { post.textContent = mine ? t('review_update') : t('review_submit'); }, 1500); return; }
      post.disabled = true;
      apiAjax('POST', 'JellyCrowd/Comments', { MediaType: item.mediaType, TmdbId: item.tmdbId, Title: item.title || '', Text: text.value.trim(), Rating: rating })
        .then(function () {
          detailReviewsLoadedId = null; // force a re-render with fresh data
          loadDetailReviews(item, panel.parentNode);
        })
        .catch(function () { post.disabled = false; });
    });
    form.appendChild(post);
    panel.appendChild(form);

    // The reviews (anonymous unless the viewer is an admin).
    (dto.Reviews || []).forEach(function (r) {
      var row = document.createElement('div');
      row.style.cssText = 'padding:.5em 0;border-top:1px solid rgba(255,255,255,.1);';
      var head = document.createElement('div');
      head.style.cssText = 'display:flex;align-items:center;gap:.5em;font-size:.9em;opacity:.85;';
      if (r.Rating > 0) { head.appendChild(reviewStarRow(r.Rating)); }
      var who = document.createElement('span');
      who.textContent = r.UserName ? r.UserName : t('review_anonymous');
      head.appendChild(who);
      row.appendChild(head);
      if (r.Text) {
        var body = document.createElement('div');
        body.textContent = r.Text;
        body.style.cssText = 'margin-top:.2em;';
        row.appendChild(body);
      }
      panel.appendChild(row);
    });

    return panel;
  }

  function loadDetailReviews(item, anchor) {
    apiAjax('GET', 'JellyCrowd/Comments/' + item.mediaType + '/' + item.tmdbId)
      .then(function (dto) {
        var existing = document.getElementById('jcDetailReviews');
        if (existing && existing.parentNode) { existing.parentNode.removeChild(existing); }
        // Only attach if we're still on the same detail page.
        if (currentDetailItemId() !== item.jellyfinId || !anchor || !anchor.isConnected) { return; }
        var panel = buildDetailReviewsPanel(item, dto || {});
        // Insert near the top of the detail content (just under the poster/synopsis block) rather than
        // at the very bottom of the page.
        if (anchor.firstChild) { anchor.insertBefore(panel, anchor.firstChild); } else { anchor.appendChild(panel); }
        detailReviewsLoadedId = item.jellyfinId;
      })
      .catch(function () { detailReviewsPendingId = null; });
  }

  // Resolve the visible detail item -> its TMDB id + type, then render reviews under the page.
  function maybeInjectDetailReviews(retries) {
    if (!commentsEnabled || !pluginVisible()) { removeDetailReviews(); return; }
    var id = currentDetailItemId();
    if (!id) { removeDetailReviews(); return; }
    if (id === detailReviewsLoadedId && document.getElementById('jcDetailReviews')) { return; }
    if (id === detailReviewsPendingId) { return; }
    if (!(window.ApiClient && window.ApiClient.getItem && window.ApiClient.getCurrentUserId)) { return; }

    var anchor = document.querySelector('.itemDetailPage:not(.hide) .detailPageContent')
      || document.querySelector('.itemDetailPage:not(.hide)')
      || document.querySelector('.detailPageContent');
    if (!anchor) {
      // The detail DOM loads asynchronously; retry a few times before giving up.
      if ((retries || 0) < 12) { setTimeout(function () { maybeInjectDetailReviews((retries || 0) + 1); }, 300); }
      return;
    }

    detailReviewsPendingId = id;
    window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), id)
      .then(function (it) {
        if (currentDetailItemId() !== id) { detailReviewsPendingId = null; return; }
        var type = it && it.Type;
        var tmdb = it && it.ProviderIds && (it.ProviderIds.Tmdb || it.ProviderIds.tmdb);
        var mediaType = type === 'Movie' ? 'movie' : (type === 'Series' ? 'tv' : null);
        if (!mediaType || !tmdb) { detailReviewsPendingId = null; return; } // not a reviewable title
        loadDetailReviews({ jellyfinId: id, mediaType: mediaType, tmdbId: parseInt(tmdb, 10) }, anchor);
      })
      .catch(function () { detailReviewsPendingId = null; });
  }

  // ---------- "Add to my library" button on the native Jellyfin detail page (§5) ----------
  // A title open in the native client is already in the library, so we offer to claim ownership of it
  // (same as the catalog popup's "Add to my library"). Defensive + idempotent like the reviews panel.
  var detailClaimLoadedId = null;
  var detailClaimPendingId = null;

  function removeDetailClaim() {
    var el = document.getElementById('jcDetailClaim');
    if (el && el.parentNode) { el.parentNode.removeChild(el); }
    detailClaimLoadedId = null;
    detailClaimPendingId = null;
  }

  function buildDetailClaim(item) {
    var wrap = document.createElement('span');
    wrap.id = 'jcDetailClaim';
    wrap.style.cssText = 'display:inline-flex;margin:.4em .6em .4em 0;vertical-align:middle;';
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.textContent = t('add_to_my_media');
    btn.title = t('claim_quota_warning');
    btn.style.cssText = 'background:#00a4dc;border:0;color:#fff;padding:.5em 1em;border-radius:.25em;cursor:pointer;font-size:.95em;';
    btn.addEventListener('click', function () {
      btn.disabled = true;
      apiAjax('POST', 'JellyCrowd/Requests/Claim', {
        TmdbId: item.tmdbId,
        MediaType: item.mediaType,
        Title: item.title
      })
        .then(function () { btn.textContent = t('added'); })
        .catch(function (e) {
          if (e && (e.status === 409 || (e.message && e.message.indexOf('409') >= 0))) { btn.textContent = t('already_yours'); }
          else { btn.disabled = false; }
        });
    });
    wrap.appendChild(btn);
    return wrap;
  }

  function maybeInjectClaimButton(retries) {
    if (!pluginVisible()) { removeDetailClaim(); return; }
    var id = currentDetailItemId();
    if (!id) { removeDetailClaim(); return; }
    if (id === detailClaimLoadedId && document.getElementById('jcDetailClaim')) { return; }
    if (id === detailClaimPendingId) { return; }
    if (!(window.ApiClient && window.ApiClient.getItem && window.ApiClient.getCurrentUserId)) { return; }

    var anchor = document.querySelector('.itemDetailPage:not(.hide) .mainDetailButtons')
      || document.querySelector('.itemDetailPage:not(.hide) .detailPageContent')
      || document.querySelector('.detailPageContent');
    if (!anchor) {
      if ((retries || 0) < 12) { setTimeout(function () { maybeInjectClaimButton((retries || 0) + 1); }, 300); }
      return;
    }

    detailClaimPendingId = id;
    window.ApiClient.getItem(window.ApiClient.getCurrentUserId(), id)
      .then(function (it) {
        if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
        var type = it && it.Type;
        var tmdb = it && it.ProviderIds && (it.ProviderIds.Tmdb || it.ProviderIds.tmdb);
        var mediaType = type === 'Movie' ? 'movie' : (type === 'Series' ? 'tv' : null);
        if (!mediaType || !tmdb) { detailClaimPendingId = null; return; } // not a claimable title
        var tmdbId = parseInt(tmdb, 10);

        function injectClaim() {
          if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
          var old = document.getElementById('jcDetailClaim');
          if (old && old.parentNode) { old.parentNode.removeChild(old); }
          if (!anchor.isConnected) { detailClaimPendingId = null; return; }
          anchor.appendChild(buildDetailClaim({ mediaType: mediaType, tmdbId: tmdbId, title: it.Name || '' }));
          detailClaimLoadedId = id;
        }

        // N32: only offer "Add to my library" if the user doesn't already own this title.
        window.ApiClient.ajax({ type: 'GET', url: getUrl('JellyCrowd/Requests/Mine'), dataType: 'json' })
          .then(function (mine) {
            if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
            var owned = (mine || []).some(function (r) {
              return r.TmdbId === tmdbId && r.MediaType === mediaType
                && (r.Status === 3 || r.Status === 'Available') && !r.DeletionRequestedAt;
            });
            if (owned) {
              removeDetailClaim();
              detailClaimLoadedId = id;
              return;
            }
            injectClaim();
          })
          .catch(injectClaim); // ownership check failed: show it anyway (the 409 still guards the claim).
      })
      .catch(function () { detailClaimPendingId = null; });
  }

  function start() {
    // Detail pages (and their review/claim anchors) render asynchronously and SPA route changes don't
    // always fire hashchange reliably — so besides the nav listeners, retry injection on DOM mutations,
    // debounced. The injectors self-guard (no-op once rendered for the current item), so this is cheap.
    var detailInjectTimer = null;
    function scheduleDetailInject() {
      if (detailInjectTimer) { return; }
      detailInjectTimer = setTimeout(function () {
        detailInjectTimer = null;
        maybeInjectDetailReviews(0);
        maybeInjectClaimButton(0);
      }, 200);
    }

    var observer = new MutationObserver(function () {
      tryInsert();
      if (overlay && overlay.style.display !== 'none') { positionOverlay(); }
      scheduleDetailInject();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    tryInsert();
    setInterval(refreshBellBadge, 15000); // M28: notif badge appears faster (Note 10)
    window.jellyCrowdRefreshBell = refreshBellBadge;
    // Any real navigation (Jellyfin menu, opening a library item) closes our overlay — except the
    // home navigation we trigger ourselves when opening a panel (N33), which must leave it open.
    function onNavClose() {
      if (suppressHashClose) {
        return; // our own open-time home navigation; the flag clears on a timer.
      }
      hideOverlay();
    }
    window.addEventListener('hashchange', onNavClose);
    window.addEventListener('popstate', onNavClose);
    // On every navigation, (re)inject internal reviews when landing on a detail page.
    function onDetailNav() {
      removeDetailReviews(); maybeInjectDetailReviews(0);
      removeDetailClaim(); maybeInjectClaimButton(0);
    }
    window.addEventListener('hashchange', onDetailNav);
    window.addEventListener('popstate', onDetailNav);
    maybeInjectDetailReviews(0); // initial load may already be a detail page
    maybeInjectClaimButton(0);
    // Catch-all: while the overlay is open, a click on anything that isn't our overlay or one of our
    // header controls / popups means the user touched the underlying Jellyfin UI -> close the overlay
    // so it never lingers when it shouldn't (native home/back/search/library, drawer, etc.).
    document.addEventListener('click', function (e) {
      if (!overlay || overlay.style.display === 'none') { return; }
      var keep = '.jellycrowd-overlay,.jellycrowd-modal-overlay,.jcHeaderNav,.jcHeaderQuota,.jcHeaderBell,.jcBellPanel,.jcHeaderAnnounce,.jcAnnouncePanel,#jcAnnEditor';
      if (e.target && e.target.closest && e.target.closest(keep)) { return; }
      hideOverlay();
    }, true);
  }

  loadConfigLang().then(loadStrings).then(start).then(resolveAdminVisibility);
})();
