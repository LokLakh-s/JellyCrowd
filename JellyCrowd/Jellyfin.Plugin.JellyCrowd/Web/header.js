/*
 * Jelly Crowd — web client shell.
 * Injected into the Jellyfin web client by Jelly Crowd's own middleware. Adds Catalog / Calendar /
 * My Requests / Dashboard entries and a compact quota bar to the top header, and hosts our user
 * pages itself in a full-screen overlay with its own tab bar (no third-party plugin). Pages render
 * inline (not in an iframe), so window.ApiClient and the active
 * theme are available to them as before. The header DOM is not a public contract, so the selectors
 * below may need tweaking per Jellyfin version.
 */
(function () {
  'use strict';

  var SUPPORTED = ['en', 'fr'];
  var strings = {};
  var cfgLang = 'auto';
  var pluginHidden = false;   // "config mode": hide the plugin from non-admins (decided server-side)

  // The user pages we host. Order defines the overlay tab order. (Moderation & Ownership are admin-only
  // and live in the plugin config page now, not the navbar.)
  var VIEWS = [
    { id: 'catalog', file: 'catalog.html', labelKey: 'nav_catalog' },
    { id: 'calendar', file: 'calendar.html', labelKey: 'nav_calendar' },
    { id: 'requests', file: 'requests.html', labelKey: 'nav_requests' },
    { id: 'dashboard', file: 'dashboard.html', labelKey: 'nav_dashboard' },
    { id: 'mymedia', file: 'mymedia.html', labelKey: 'my_media_title' },
    { id: 'admin', file: 'admin.html', labelKey: 'nav_admin' },
    // No navbar button: reached from the avatar menu (where Jellyfin keeps its own preferences) and
    // from the bell panel, which is where someone looks when they want to change notifications.
    { id: 'preferences', file: 'preferences.html', labelKey: 'prefs_title' }
  ];

  var overlay = null;
  var viewHost = null;
  var focusBeforeOverlay = null;   // element focused when the overlay opened, restored on close
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
      var active = key === id;
      headerNavButtons[key].style.color = active ? NAV_WHITE : NAV_GREY;
      // Colour alone does not tell a screen reader which section is open.
      if (active) {
        headerNavButtons[key].setAttribute('aria-current', 'page');
      } else {
        headerNavButtons[key].removeAttribute('aria-current');
      }
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
  var guideUrl = '';          // admin opt-in: user-guide link shown as a header icon ('' = hidden)
  var hideNativeDrawer = false; // admin opt-in: hide Jellyfin's left drawer for non-admins
  var jcSkipOutro = false;    // whether the smart Skip Outro control is enabled (install the watcher if so)

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
        guideUrl = (d && d.GuideLinkUrl) ? String(d.GuideLinkUrl) : '';
        jcSkipOutro = !!(d && d.SkipOutroEnabled);
        hideNativeDrawer = !!(d && d.HideNativeDrawer);
        applyDrawerHiding();
      })
      .catch(function () { /* keep defaults on failure */ });
  }

  // Hide Jellyfin's left navigation drawer for non-admins when the admin opted in. Admins always keep it.
  // Applied via a body class so it re-evaluates whenever admin status or the setting is (re)resolved.
  function applyDrawerHiding() {
    if (document.body) {
      document.body.classList.toggle('jc-hide-native-drawer', hideNativeDrawer && !isAdmin);
    }
  }

  // Confirm via the authenticated endpoint whether THIS user is an admin (and, in config mode, exempt).
  // Retried a few times because ApiClient may not be ready at first paint.
  function resolveAdminVisibility(attempt) {
    attempt = attempt || 0;
    apiAjax('GET', 'JellyCrowd/Settings/Visibility')
      .then(function (d) {
        isAdmin = !!(d && d.IsAdmin === true);
        applyDrawerHiding(); // now that admin status is known, an admin keeps the drawer
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
    // Anchor the overlay below whichever header is actually on screen: the MUI app bar (Jellyfin 12) or
    // the classic skinHeader (10.11). On 12.0 the skinHeader still exists but is hidden (height 0), so we
    // must prefer the MUI bar — otherwise the overlay starts at y=0 and overlaps the toolbar.
    var header = document.querySelector('.MuiAppBar-root') || document.querySelector('.MuiToolbar-root')
      || document.querySelector('.skinHeader');
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

    // The focus helpers live in catalog.lib.js, which the base page does not load on its own. Pull it in
    // as the overlay is built so the trap is armed well before anyone reaches for Tab.
    if (!window.JellyCrowdLib) {
      var libEl = document.createElement('script');
      libEl.src = getUrl('JellyCrowd/Web/catalog.lib.js');
      document.head.appendChild(libEl);
    }

    window.addEventListener('resize', positionOverlay);
    document.addEventListener('keydown', function (e) {
      if (overlay.style.display === 'none') {
        return;
      }

      if (e.key === 'Escape') {
        hideOverlay();
        return;
      }

      // Keep Tab inside the dialog. aria-modal alone tells assistive tech the rest of the page is inert;
      // it does not stop the browser tabbing into it, so a keyboard user would walk out of the overlay
      // into a page they cannot see.
      if (e.key === 'Tab' && window.JellyCrowdLib) {
        window.JellyCrowdLib.handleTrapKeydown(e, overlay);
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
    var freshOpen = overlay.style.display === 'none';
    if (freshOpen) {
      sendBackgroundHome();
      // Remember where focus was so closing puts it back on the control that opened us.
      focusBeforeOverlay = document.activeElement;
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

    // Move focus into the dialog on a fresh open, once it is displayed and has layout.
    if (freshOpen && window.JellyCrowdLib) {
      window.JellyCrowdLib.focusFirst(overlay);
    }

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

    // Hand focus back where it came from. Without this it falls to <body> and the next Tab restarts
    // from the top of the page.
    var target = window.JellyCrowdLib
      ? window.JellyCrowdLib.focusRestoreTarget(focusBeforeOverlay, document)
      : null;
    focusBeforeOverlay = null;
    if (target) {
      target.focus();
    }
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
    a.setAttribute('data-jc-view', viewId);
    headerNavButtons[viewId] = a;
    a.style.color = (viewId === activeNavId) ? NAV_WHITE : NAV_GREY;
    // Hover turns blue (Jellyfin accent); on leave restore the selected/unselected colour.
    a.addEventListener('mouseenter', function () { a.style.color = NAV_BLUE; });
    a.addEventListener('mouseleave', function () { a.style.color = (viewId === activeNavId) ? NAV_WHITE : NAV_GREY; });
    // stopPropagation: keep the click from reaching Jellyfin's tab-bar click handler.
    a.addEventListener('click', function (e) { e.stopPropagation(); toggleView(viewId); });
    return a;
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
  // The admin-only "Admin" tab is added once admin status is known (it can resolve after the first
  // insertNav), so this runs again from tryInsert() to top up an already-built nav.
  function ensureAdminNav(nav) {
    // Guard on the live nav DOM, not the global headerNavButtons map: on 10.11 Jellyfin re-renders the
    // tabs row and our nav is rebuilt fresh, but the map still holds a stale (detached) admin button —
    // which previously made the rebuilt nav skip the Admin tab.
    if (isAdmin && !nav.querySelector('[data-jc-view="admin"]')) {
      nav.appendChild(navButton('nav_admin', 'admin'));
    }
  }

  function insertNav() {
    var existingNav = document.querySelector('.jcHeaderNav');
    if (existingNav) {
      ensureAdminNav(existingNav); // admin status may have resolved since the first insert
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
    nav.appendChild(navButton('nav_dashboard', 'dashboard'));
    nav.appendChild(navButton('nav_catalog', 'catalog'));
    nav.appendChild(navButton('nav_calendar', 'calendar'));
    nav.appendChild(navButton('nav_requests', 'requests'));
    ensureAdminNav(nav);
    // Sit on the same line as the real tabs when the slider exists, else in the row/host itself.
    var slider = tabs ? tabs.querySelector('.emby-tabs-slider') : null;
    (slider || host).appendChild(nav);
  }

  // ---------- Jellyfin 12 (React + MUI) header ----------
  // The 12.0 web client is a React/MUI app: the old `.headerTabs`/`.skinHeader` still exist but are
  // hidden, so the classic insertNav() injects into dead DOM. Here we additionally inject our tabs into
  // the live MUI toolbar. Styling is borrowed by cloning an existing native nav link's className (the
  // emotion `css-*` hashes are build-specific, so we read them off the live element rather than hard-code
  // them). React re-renders the toolbar, so a MutationObserver re-injects when our tabs disappear.
  // No-op on 10.11 (there is no `.MuiToolbar-root`).
  var muiObserver = null;
  var muiPending = false;

  function muiNavItems() {
    var items = [
      { id: 'dashboard', labelKey: 'nav_dashboard' },
      { id: 'catalog', labelKey: 'nav_catalog' },
      { id: 'calendar', labelKey: 'nav_calendar' },
      { id: 'requests', labelKey: 'nav_requests' }
    ];
    if (isAdmin) {
      items.push({ id: 'admin', labelKey: 'nav_admin' });
    }
    return items;
  }

  function insertMuiNav() {
    if (!pluginVisible()) {
      return;
    }
    var toolbar = document.querySelector('.MuiToolbar-root');
    if (!toolbar) {
      return;
    }
    var stack = toolbar.querySelector('.MuiStack-root');
    if (!stack) {
      return;
    }
    // Clone the className of an existing native nav link (Movies / a tab link) so our tabs match exactly.
    var template = stack.querySelector('a[href*="/movies"]') || stack.querySelector('a[href*="tab="]')
      || stack.querySelectorAll('a')[1] || stack.querySelector('a');
    var cls = template ? template.className : '';
    muiNavItems().forEach(function (item) {
      if (stack.querySelector('[data-jc-nav="' + item.id + '"]')) {
        return; // already present
      }
      var a = document.createElement('a');
      a.className = cls;
      a.href = '#';
      a.setAttribute('data-jc-nav', item.id);
      a.textContent = t(item.labelKey);
      a.addEventListener('click', function (e) { e.preventDefault(); e.stopPropagation(); toggleView(item.id); });
      stack.appendChild(a);
    });
  }

  // Right-cluster host: the MUI toolbar's icon box (next to Search) on Jellyfin 12, else the classic
  // .headerRight on 10.11. Used by the quota / bell / announcement / links inserters so the same builders
  // serve both layouts.
  function rightHost() {
    return document.querySelector('.MuiToolbar-root .MuiBox-root') || document.querySelector('.headerRight');
  }

  // The className of a native MUI icon button (Search) so our injected icon buttons match it exactly.
  // Empty on 10.11 (no MUI toolbar) → callers fall back to the classic paper-icon-button-light styling.
  function muiIconButtonClass() {
    var tb = document.querySelector('.MuiToolbar-root');
    if (!tb) {
      return '';
    }
    var s = tb.querySelector('a[aria-label="Search"], button[aria-label="Search"]');
    return s ? s.className : '';
  }

  // (Re)inject our whole header UI into the MUI toolbar. Each inserter is idempotent (host-scoped guard).
  function mountMui() {
    insertMuiNav();
    insertCatalogSearch();
    insertQuota();
    insertBell();
    insertAnnouncement();
    insertHeaderLinks();
    labelNativeSearch();
  }

  // Clarify that the native magnifier searches the Jellyfin library (as opposed to our catalog magnifier).
  function labelNativeSearch() {
    var s = document.querySelector('.MuiToolbar-root a[aria-label="Search"], .MuiToolbar-root button[aria-label="Search"]');
    if (s) { s.title = t('search_jellyfin_title'); }
  }

  // A second, distinct magnifier that searches the JellyCrowd catalog (TMDB), next to the native one that
  // searches the existing library. Opens the catalog view and focuses its search box.
  function insertCatalogSearch() {
    if (!pluginVisible()) {
      return;
    }
    var host = rightHost();
    if (!host || host.querySelector('.jcHeaderSearch')) {
      return;
    }

    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderSearch';
    wrap.style.cssText = 'position:relative;display:inline-flex;align-items:center;align-self:center;';
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = muiIconButtonClass() || 'paper-icon-button-light headerButton';
    btn.title = t('search_catalog_title');
    btn.setAttribute('aria-label', t('search_catalog_title'));
    var icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = 'manage_search'; // distinct from the native "search" glyph
    btn.appendChild(icon);
    btn.addEventListener('click', function (e) {
      e.stopPropagation();
      showView('catalog');
      // Focus the catalog search box once the view is present.
      setTimeout(function () {
        var input = document.getElementById('jcSearchInput');
        if (input) { input.focus(); }
      }, 150);
    });
    wrap.appendChild(btn);
    host.insertBefore(wrap, host.querySelector('.jcHeaderBell') || host.querySelector('.jcHeaderQuota') || null);
  }

  function watchMuiToolbar() {
    if (muiObserver || !document.querySelector('.MuiToolbar-root')) {
      return;
    }
    muiObserver = new MutationObserver(function () {
      if (muiPending) {
        return;
      }
      muiPending = true;
      // Debounce: React can fire many mutations per render; coalesce and re-inject only when our stuff is
      // gone (React replaced the toolbar). The nav tabs and the quota bar are the two sentinels.
      setTimeout(function () {
        muiPending = false;
        var tb = document.querySelector('.MuiToolbar-root');
        if (!tb) {
          return;
        }
        var stack = tb.querySelector('.MuiStack-root');
        var box = tb.querySelector('.MuiBox-root');
        var navMissing = stack && !stack.querySelector('[data-jc-nav]');
        var clusterMissing = box && !box.querySelector('.jcHeaderQuota');
        if (navMissing || clusterMissing) {
          mountMui();
        }
      }, 150);
    });
    muiObserver.observe(document.body, { childList: true, subtree: true });
  }

  // Brand SVGs for the optional header links. Inline (the base page can't load external assets, and a
  // strict CSP would block remote images anyway). `currentColor` so they inherit the header text colour.
  var DISCORD_SVG = '<svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" aria-hidden="true"><path d="M20.317 4.369A19.79 19.79 0 0 0 15.885 3c-.21.375-.45.88-.617 1.28a18.27 18.27 0 0 0-5.535 0A12.6 12.6 0 0 0 9.11 3 19.7 19.7 0 0 0 4.677 4.37C1.99 8.38 1.26 12.29 1.62 16.14a19.9 19.9 0 0 0 6.07 3.06c.49-.67.93-1.38 1.3-2.13-.71-.27-1.39-.6-2.03-.99.17-.13.34-.26.5-.4 3.93 1.84 8.18 1.84 12.06 0 .16.14.33.27.5.4-.65.39-1.33.72-2.04.99.37.75.81 1.46 1.3 2.13a19.84 19.84 0 0 0 6.07-3.06c.42-4.46-.73-8.34-3.05-11.77ZM8.52 13.79c-1.18 0-2.15-1.08-2.15-2.41 0-1.33.95-2.42 2.15-2.42 1.21 0 2.18 1.09 2.16 2.42 0 1.33-.95 2.41-2.16 2.41Zm6.96 0c-1.18 0-2.15-1.08-2.15-2.41 0-1.33.95-2.42 2.15-2.42 1.21 0 2.18 1.09 2.16 2.42 0 1.33-.95 2.41-2.16 2.41Z"/></svg>';
  var SUPPORT_SVG = '<svg viewBox="0 0 16 16" width="24" height="24" fill="currentColor" aria-hidden="true"><path d="M5.5 9.511c.076.954.83 1.697 2.182 1.785V12h.6v-.709c1.4-.098 2.218-.846 2.218-1.932 0-.987-.626-1.496-1.745-1.76l-.473-.112V5.57c.6.068.982.396 1.074.85h1.052c-.076-.919-.864-1.638-2.126-1.716V4h-.6v.719c-1.195.117-2.01.836-2.01 1.853 0 .9.606 1.472 1.613 1.707l.397.098v2.034c-.615-.093-1.022-.43-1.114-.9H5.5zm2.177-2.166c-.59-.137-.91-.416-.91-.836 0-.47.345-.822.915-.925v1.76h-.005zm.692 1.193c.717.166 1.048.435 1.048.91 0 .542-.412.914-1.135.982V8.518l.087.02z"/><path d="M8 13.5a5.5 5.5 0 1 1 0-11 5.5 5.5 0 0 1 0 11zm0 .5A6 6 0 1 0 8 2a6 6 0 0 0 0 12z"/></svg>';
  // A circled question mark for the optional user-guide link.
  var GUIDE_SVG = '<svg viewBox="0 0 24 24" width="24" height="24" fill="currentColor" aria-hidden="true"><path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16Z"/><path d="M12 6.4c-1.72 0-2.94.9-3.42 2.4l1.63.62c.24-.72.73-1.22 1.6-1.22.82 0 1.42.5 1.42 1.2 0 .58-.32.92-1.02 1.44-.82.6-1.24 1.13-1.24 2.12v.4h1.72v-.32c0-.6.3-.94 1.02-1.46.82-.6 1.32-1.24 1.32-2.35 0-1.6-1.3-2.63-3.05-2.63Z"/><circle cx="12" cy="16.5" r="1.15"/></svg>';

  function brandLinkIcon(svg, href, title, cls) {
    var a = document.createElement('a');
    // Same native icon-button classes as the bell/announcement so colour, hover circle, size and
    // alignment all match them. On Jellyfin 12 we clone the MUI icon-button class; on 10.11 we fall back
    // to the classic paper-icon-button-light styling.
    a.className = (muiIconButtonClass() || 'paper-icon-button-light headerButton') + ' jcHeaderLink ' + cls;
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.title = title;
    a.setAttribute('aria-label', title);
    a.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;text-decoration:none;color:inherit;';
    a.innerHTML = svg;
    a.addEventListener('click', function (e) { e.stopPropagation(); });
    return a;
  }

  // Optional admin-configured links in the RIGHT header cluster: just LEFT of the announcement icon
  // (and thus right of the native search button), so they line up with the bell/announcement/quota.
  // Idempotent (admin config can resolve after the first insert) and gated on a configured URL.
  function insertHeaderLinks() {
    if (!discordUrl && !supportUrl && !guideUrl) {
      return;
    }
    var host = rightHost();
    if (!host) {
      return;
    }
    var group = host.querySelector('.jcHeaderLinks');
    if (!group) {
      group = document.createElement('span');
      group.className = 'jcHeaderLinks';
      // align-self:center like the other injected icons; the top nudge is applied via CSS below.
      group.style.cssText = 'display:inline-flex;align-items:center;align-self:center;';
      var before = host.querySelector('.jcHeaderAnnounce') || host.querySelector('.jcHeaderBell')
        || host.querySelector('.jcHeaderQuota') || host.querySelector('.headerUserButton');
      host.insertBefore(group, before || null);
    }
    if (discordUrl && !group.querySelector('.jcHeaderLink-discord')) {
      group.appendChild(brandLinkIcon(DISCORD_SVG, discordUrl, t('discord_link_title'), 'jcHeaderLink-discord'));
    }
    if (supportUrl && !group.querySelector('.jcHeaderLink-support')) {
      group.appendChild(brandLinkIcon(SUPPORT_SVG, supportUrl, t('support_link_title'), 'jcHeaderLink-support'));
    }
    if (guideUrl && !group.querySelector('.jcHeaderLink-guide')) {
      group.appendChild(brandLinkIcon(GUIDE_SVG, guideUrl, t('guide_link_title'), 'jcHeaderLink-guide'));
    }
  }

  // ---------- Avatar dropdown ----------
  // Clicking the header avatar opens our own popover instead of navigating to the native prefs page: a
  // replica of the native "My preferences" links (same client routes), admin shortcuts, native SyncPlay /
  // Cast triggers, the optional Discord / Support links, and sign out. tryInsert() re-wires the button
  // whenever the client rebuilds the header.
  var avatarMenu = null;

  function closeAvatarMenu() {
    if (!avatarMenu) { return; }
    avatarMenu.remove();
    avatarMenu = null;
    document.removeEventListener('click', onAvatarDocClick, true);
    document.removeEventListener('keydown', onAvatarKey, true);
    window.removeEventListener('resize', closeAvatarMenu);
  }

  function onAvatarDocClick(e) { if (avatarMenu && !avatarMenu.contains(e.target)) { closeAvatarMenu(); } }
  function onAvatarKey(e) { if (e.key === 'Escape') { closeAvatarMenu(); } }

  function clickNative(sel) { var el = document.querySelector(sel); if (el) { el.click(); } }

  // Match the native "Sign Out": invalidate the session, then return to the login screen. Defensive
  // across versions — prefer the app's own logout, else ApiClient + reload.
  function jcLogout() {
    try { if (window.Dashboard && typeof window.Dashboard.logout === 'function') { window.Dashboard.logout(); return; } } catch (e) { /* fall through */ }
    try {
      if (window.ApiClient && window.ApiClient.logout) {
        window.ApiClient.logout().then(function () { window.location.reload(); }, function () { window.location.reload(); });
        return;
      }
    } catch (e) { /* fall through */ }
    window.location.reload();
  }

  function avatarItem(icon, label, href, onClick, newTab) {
    var el = document.createElement(href ? 'a' : 'button');
    el.className = 'jcAvatarItem';
    if (href) { el.href = href; if (newTab) { el.target = '_blank'; el.rel = 'noopener noreferrer'; } }
    else { el.type = 'button'; }
    var ic = document.createElement('span');
    ic.className = 'material-icons jcAvatarItemIcon';
    ic.setAttribute('aria-hidden', 'true');
    ic.textContent = icon;
    var tx = document.createElement('span');
    tx.textContent = label;
    el.appendChild(ic);
    el.appendChild(tx);
    el.addEventListener('click', function () { if (onClick) { onClick(); } closeAvatarMenu(); });
    return el;
  }

  function avatarSep() { var s = document.createElement('div'); s.className = 'jcAvatarSep'; return s; }

  function buildAvatarMenu(btn) {
    var menu = document.createElement('div');
    menu.className = 'jcAvatarMenu';
    var uid = (window.ApiClient && window.ApiClient.getCurrentUserId && window.ApiClient.getCurrentUserId()) || '';
    var q = uid ? ('?userId=' + encodeURIComponent(uid)) : '';

    var head = document.createElement('div');
    head.className = 'jcAvatarMenuHead';
    head.textContent = (btn && (btn.title || btn.getAttribute('title'))) || '';
    if (head.textContent) { menu.appendChild(head); }

    // Ours first, then the native "My preferences" replica: someone looking for "where do I change my
    // notifications" opens this menu, and it belongs beside Jellyfin's own preference entries.
    menu.appendChild(avatarItem('notifications', t('prefs_nav'), null, function () { showView('preferences'); }));
    menu.appendChild(avatarSep());

    // Native "My preferences" replica — same client routes Jellyfin uses (stable across 10.x / 12).
    [
      ['person', t('avm_profile'), '#/userprofile' + q],
      ['flash_on', t('avm_quickconnect'), '#/quickconnect' + q],
      ['tv', t('avm_display'), '#/mypreferencesdisplay' + q],
      ['home', t('avm_home'), '#/mypreferenceshome' + q],
      ['play_arrow', t('avm_playback'), '#/mypreferencesplayback' + q],
      ['closed_caption', t('avm_subtitles'), '#/mypreferencessubtitles' + q],
      ['tune', t('avm_controls'), '#/mypreferencescontrols' + q]
    ].forEach(function (r) { menu.appendChild(avatarItem(r[0], r[1], r[2])); });

    if (isAdmin) {
      menu.appendChild(avatarSep());
      menu.appendChild(avatarItem('dashboard', t('avm_dashboard'), '#/dashboard'));
      menu.appendChild(avatarItem('mode_edit', t('avm_metadata'), '#/metadata'));
    }

    // Native SyncPlay / Cast — trigger the real header buttons so behaviour is 100% native. Their own
    // header buttons are hidden (see injectHeaderStyle) so these are the single way in, no duplication.
    menu.appendChild(avatarSep());
    menu.appendChild(avatarItem('group', t('avm_syncplay'), null, function () { clickNative('.headerSyncButton'); }));
    menu.appendChild(avatarItem('cast', t('avm_cast'), null, function () { clickNative('.headerCastButton'); }));

    // Discord / support / guide are NOT repeated here — they live in the header bar (jcHeaderLinks).

    menu.appendChild(avatarSep());
    menu.appendChild(avatarItem('logout', t('avm_signout'), null, jcLogout));
    return menu;
  }

  function toggleAvatarMenu(btn) {
    if (avatarMenu) { closeAvatarMenu(); return; }
    avatarMenu = buildAvatarMenu(btn);
    document.body.appendChild(avatarMenu);
    var r = btn.getBoundingClientRect();
    avatarMenu.style.top = Math.round(r.bottom + 6) + 'px';
    avatarMenu.style.right = Math.max(8, Math.round(window.innerWidth - r.right)) + 'px';
    // Defer so the opening click doesn't immediately dismiss it.
    setTimeout(function () {
      document.addEventListener('click', onAvatarDocClick, true);
      document.addEventListener('keydown', onAvatarKey, true);
      window.addEventListener('resize', closeAvatarMenu);
    }, 0);
  }

  function installAvatarMenu() {
    var btn = document.querySelector('.headerUserButton');
    if (!btn || btn.getAttribute('data-jc-avatar') === '1') { return; }
    btn.setAttribute('data-jc-avatar', '1');
    // Capture + stopImmediatePropagation so Jellyfin's own handler never navigates to the prefs page.
    btn.addEventListener('click', function (e) {
      e.preventDefault();
      e.stopImmediatePropagation();
      toggleAvatarMenu(btn);
    }, true);
  }

  // Quota bar lives in .headerRight, placed between the search icon and the user avatar
  // (i.e. just before the .headerUserButton), per request.
  function insertQuota() {
    var host = rightHost();
    if (!host || host.querySelector('.jcHeaderQuota')) {
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
    gear.setAttribute('aria-label', t('notif_settings'));
    gear.textContent = '⚙';
    gear.style.cssText = 'background:none;border:0;color:#fff;cursor:pointer;font-size:1em;opacity:.8;';
    // Opens the preferences page rather than a form crammed into this dropdown.
    gear.addEventListener('click', function () { panel.style.display = 'none'; showView('preferences'); });
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
    var host = rightHost();
    if (!host || host.querySelector('.jcHeaderBell')) {
      return;
    }
    // The popover lives on <body>; if React wiped a previous bell button its panel could linger, so drop
    // any orphaned panel before building a fresh one (avoids duplicates across re-renders).
    var stalePanels = document.querySelectorAll('.jcBellPanel');
    for (var sp = 0; sp < stalePanels.length; sp++) { stalePanels[sp].remove(); }

    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderBell';
    // align-self:center so the bell is vertically centred in the header bar like the nav tabs (it sat
    // slightly low otherwise, not inheriting the row's centering).
    wrap.style.cssText = 'position:relative;display:inline-flex;align-items:center;align-self:center;';

    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = muiIconButtonClass() || 'paper-icon-button-light headerButton';
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
    badge.style.cssText = 'position:absolute;top:.05em;right:.05em;min-width:.95em;height:.95em;padding:0 .2em;border-radius:.5em;background:#e53935;color:#fff;font-size:.55em;line-height:.95em;text-align:center;display:none;box-sizing:border-box;z-index:1;pointer-events:none;';
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
    // Close on a click anywhere outside the panel and its button. Capture phase so it fires even when
    // the clicked element stops propagation (nav tabs, avatar menu, …) — a bubble-phase listener missed
    // those and left the panel stuck open.
    document.addEventListener('click', function (e) {
      if (panel.style.display !== 'none' && !panel.contains(e.target) && !btn.contains(e.target)) {
        panel.style.display = 'none';
      }
    }, true);

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
    // Keep the icon white like the rest of the header; only tint it for warning/alert levels so those
    // still stand out. (Green "info" stays white — a green icon among white ones looked out of place.)
    var lvl = announcement.level;
    els.icon.style.color = (hasText && (lvl === 'yellow' || lvl === 'red')) ? announcementColors(lvl).bg : '';
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
    var host = rightHost();
    if (!host || host.querySelector('.jcHeaderAnnounce')) { return; }
    // Drop any orphaned popover left over from a React re-render before building a fresh one.
    var stalePanels = document.querySelectorAll('.jcAnnouncePanel');
    for (var sp = 0; sp < stalePanels.length; sp++) { stalePanels[sp].remove(); }

    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderAnnounce';
    wrap.style.cssText = 'position:relative;display:inline-flex;align-items:center;align-self:center;';

    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = muiIconButtonClass() || 'paper-icon-button-light headerButton';
    btn.title = t('announcement_title');
    btn.style.cssText = 'position:relative;overflow:visible;';
    var icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = 'campaign';
    btn.appendChild(icon);

    var dot = document.createElement('span');
    dot.className = 'jcAnnounceDot';
    dot.style.cssText = 'position:absolute;top:.1em;right:.1em;width:.5em;height:.5em;border-radius:50%;background:#e53935;display:none;box-sizing:border-box;z-index:1;pointer-events:none;';
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
    // Close on a click anywhere outside the panel and its button. Capture phase so it fires even when
    // the clicked element stops propagation (nav tabs, avatar menu, …) — a bubble-phase listener missed
    // those and left the panel stuck open.
    document.addEventListener('click', function (e) {
      if (panel.style.display !== 'none' && !panel.contains(e.target) && !btn.contains(e.target)) {
        panel.style.display = 'none';
      }
    }, true);

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
      // Our nav buttons drop the native outline to sit flush with Jellyfin's tabs; give keyboard users
      // the indicator back on :focus-visible (mouse clicks stay clean).
      '.jcHeaderTab:focus-visible,.jellycrowd-overlay button:focus-visible{outline:2px solid #00a4dc;outline-offset:2px;border-radius:2px;}' +
      // Respect the OS "reduce motion" setting: no transitions or animations anywhere we own.
      '@media (prefers-reduced-motion: reduce){.jellycrowd-overlay,.jellycrowd-overlay *,.jcHeaderTab{transition:none !important;animation:none !important;scroll-behavior:auto !important;}}' +
      '.headerTabs .emby-tab-button{display:none !important;}' +
      // De-duplicate the header: SyncPlay/Groups and Cast are reachable from the avatar menu, so hide
      // their native header buttons (the menu shortcuts still click them programmatically).
      '.skinHeader .headerSyncButton,.skinHeader .headerCastButton{display:none !important;}' +
      // Admin opt-in: hide the native left drawer (hamburger) for non-admins (body class set in JS).
      'body.jc-hide-native-drawer .mainDrawerButton{display:none !important;}' +
      // Some library types (Other/Books) hide the empty tab row — keep it shown when it hosts our nav.
      '.headerTabs:has(.jcHeaderNav){display:flex !important;justify-content:center;}' +
      // The header logo / home button is a link to Home — show it as one (pointer cursor on hover).
      '.skinHeader .headerHomeButton,.skinHeader .pageTitleWithLogo,.skinHeader .pageTitle{cursor:pointer;}' +
      // The nav tabs row sits a few px higher than the rest of the bar; nudge our injected elements
      // (announcement, bell, quota) up to line up with it.
      '.skinHeader .jcHeaderLinks,.skinHeader .jcHeaderAnnounce,.skinHeader .jcHeaderBell,.skinHeader .jcHeaderQuota{position:relative;top:-4px;}' +
      // Compact bullet lists inside the (markdown) announcement.
      '.jcAnnounceText ul{margin:.15em 0;padding-left:1.1em;}.jcAnnounceText li{margin:0;}' +
      // During fullscreen video playback (incl. a local-intro pre-roll) the plugin's header chrome must
      // not overlay the video — mirror how Jellyfin hides its own header. Keyed on the video player
      // container, present only while a video plays; covers both layouts (.jcHeaderNav on 10.11,
      // [data-jc-nav] links on 12) and hides the group parents (which also hides their tabs/links).
      'body:has(.videoPlayerContainer) .jcHeaderNav,' +
      'body:has(.videoPlayerContainer) [data-jc-nav],' +
      'body:has(.videoPlayerContainer) .jcHeaderLinks,' +
      'body:has(.videoPlayerContainer) .jcHeaderBell,' +
      'body:has(.videoPlayerContainer) .jcHeaderQuota,' +
      'body:has(.videoPlayerContainer) .jcHeaderAnnounce,' +
      'body:has(.videoPlayerContainer) #jcBrandLogo{display:none !important;}' +
      // The login page shows the header shell but no user is signed in yet — hide all of our chrome (nav
      // tabs, links, bell, quota, announcement) there so a logged-out visitor never sees plugin controls.
      'body:has(#loginPage:not(.hide)) .jcHeaderNav,' +
      'body:has(#loginPage:not(.hide)) [data-jc-nav],' +
      'body:has(#loginPage:not(.hide)) .jcHeaderLinks,' +
      'body:has(#loginPage:not(.hide)) .jcHeaderLink,' +
      'body:has(#loginPage:not(.hide)) .jcHeaderBell,' +
      'body:has(#loginPage:not(.hide)) .jcHeaderQuota,' +
      'body:has(#loginPage:not(.hide)) .jcHeaderAnnounce{display:none !important;}' +
      // On a phone the header is too narrow for everything, and it can't scroll — items get cut off and
      // become unreachable (esp. the "My library" box). Drop the Discord/Support links (they're also in the
      // avatar menu) to free room, and let the header strip scroll sideways as a fallback so nothing is lost.
      '@media (max-width:600px){' +
      '.jcHeaderLink{display:none !important;}' +
      '.MuiToolbar-root,.headerTabs{overflow-x:auto;scrollbar-width:none;}' +
      '.MuiToolbar-root::-webkit-scrollbar,.headerTabs::-webkit-scrollbar{display:none;}' +
      '}' +
      // Avatar dropdown popover.
      '.jcAvatarMenu{position:fixed;z-index:10000;min-width:15em;max-width:min(92vw,20em);background:#1a1a1a;color:#fff;border-radius:.45em;box-shadow:0 8px 30px rgba(0,0,0,.55);padding:.4em 0;font-size:.95em;max-height:82vh;overflow-y:auto;}' +
      '.jcAvatarMenuHead{padding:.55em 1.2em .5em;opacity:.6;font-size:.78em;text-transform:uppercase;letter-spacing:.05em;font-weight:600;}' +
      '.jcAvatarItem{display:flex;align-items:center;gap:.95em;width:100%;box-sizing:border-box;padding:.62em 1.2em;background:none;border:0;color:#fff;text-decoration:none;cursor:pointer;font:inherit;font-size:1em;text-align:left;}' +
      '.jcAvatarItem:hover,.jcAvatarItem:focus{background:rgba(255,255,255,.1);outline:none;}' +
      '.jcAvatarItemIcon{font-size:1.35em;opacity:.85;flex:0 0 auto;}' +
      '.jcAvatarSep{height:1px;background:rgba(255,255,255,.13);margin:.35em 0;}';
    document.head.appendChild(style);
  }

  // In "config mode" the plugin is hidden from non-admins — in that case we must NOT hide the native
  // tabs, otherwise those users get an empty header (our nav isn't inserted to fill it).
  function removeHeaderStyle() {
    var style = document.getElementById('jcHeaderStyle');
    if (style && style.parentNode) { style.parentNode.removeChild(style); }
  }

  // ---------- Branding (cosmetic theming applied to the whole UI, for every visitor) ----------
  // Settings come from the admin Branding tab via the anonymous /Settings/Branding endpoint and are
  // applied at runtime: a <style> block (built by the shared lib, loaded on demand), a favicon swap, a
  // navbar logo, a best-effort default avatar, and custom left-drawer entries. Re-asserted on DOM
  // mutations (the web client re-renders), each step idempotent so it never thrashes. Independent of
  // plugin visibility — branding is site-wide.
  var branding = null;        // last-fetched branding settings
  var brandingCss = null;     // last applied css text (skip redundant writes)
  var brandingLib = null;     // cached JellyCrowdLib once loaded (the base page doesn't load it otherwise)

  // Resolve a branding image reference: an external http(s) or root-relative URL is used as-is; a relative
  // plugin path (an uploaded image, "JellyCrowd/Branding/Image/…") is resolved against the server.
  function brandingAssetUrl(u) {
    if (!u) { return u; }
    if (/^(https?:)?\/\//i.test(u) || u.charAt(0) === '/' || /^data:/i.test(u)) { return u; }
    return getUrl(u);
  }

  function loadBranding() {
    return fetch(getUrl('JellyCrowd/Settings/Branding'))
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) {
        branding = d || null;
        if (branding) {
          // Normalise uploaded-image paths to absolute so both the <style> builder and the imperative
          // logo/favicon/avatar steps get a usable URL.
          branding.LogoUrl = brandingAssetUrl(branding.LogoUrl);
          branding.FaviconUrl = brandingAssetUrl(branding.FaviconUrl);
          branding.DefaultAvatarUrl = brandingAssetUrl(branding.DefaultAvatarUrl);
          branding.BackgroundUrl = brandingAssetUrl(branding.BackgroundUrl);
        }
      })
      .catch(function () { branding = null; });
  }

  function brandingEnabled() { return !!(branding && branding.Enabled); }

  // Load catalog.lib.js once (only when branding is on), then re-apply; subsequent calls are synchronous.
  function ensureBrandingLib(cb) {
    if (brandingLib) { cb(brandingLib); return; }
    if (window.JellyCrowdLib) { brandingLib = window.JellyCrowdLib; cb(brandingLib); return; }
    if (document.getElementById('jcBrandingLib')) { return; } // load in flight; its onload re-applies
    var s = document.createElement('script');
    s.id = 'jcBrandingLib';
    s.src = getUrl('JellyCrowd/Web/catalog.lib.js');
    s.onload = function () { brandingLib = window.JellyCrowdLib || null; applyBranding(); };
    document.head.appendChild(s);
  }

  function removeBranding() {
    var s = document.getElementById('jcBrandingStyle');
    if (s && s.parentNode) { s.parentNode.removeChild(s); }
    var logo = document.getElementById('jcBrandLogo');
    if (logo && logo.parentNode) { logo.parentNode.removeChild(logo); }
    var links = document.querySelectorAll('.jcDrawerLink');
    for (var i = 0; i < links.length; i++) { if (links[i].parentNode) { links[i].parentNode.removeChild(links[i]); } }
    brandingCss = null;
  }

  function applyBrandingStyle() {
    ensureBrandingLib(function (L) {
      var css = L.buildBrandingCss(branding);
      // Structural rules the pure builder doesn't own (sized to the runtime-injected elements):
      if (branding.LogoUrl) {
        css += '\n.jcBrandLogo{height:1.7em;width:auto;cursor:pointer;margin:0 .5em;vertical-align:middle;}';
        // Replace the native Jellyfin header logo instead of showing a second one beside it.
        css += '\n.pageTitleWithLogo{display:none !important;}';
      }
      if (branding.DefaultAvatarUrl) {
        // Best-effort: paint the configured image over the placeholder shown for users with no photo.
        css += '\n.headerUserButtonRound .material-icons,.userButtonIcon,.cardImageIcon.person{'
          + 'background-image:url("' + branding.DefaultAvatarUrl + '") !important;background-size:cover !important;'
          + 'background-position:center !important;color:transparent !important;border-radius:50%;}';
      }
      if (branding.BackgroundUrl || branding.BackgroundColor) {
        // Extend the custom background onto the Jelly Crowd overlay panels too.
        var jcBg = (branding.BackgroundColor ? 'background-color:' + branding.BackgroundColor + ' !important;' : '')
          + (branding.BackgroundUrl ? 'background-image:url("' + branding.BackgroundUrl + '") !important;background-size:cover !important;background-position:center !important;background-attachment:fixed !important;' : '');
        css += '\n.jellycrowd-overlay{' + jcBg + '}';
      }
      if (css === brandingCss && document.getElementById('jcBrandingStyle')) { return; }
      brandingCss = css;
      var style = document.getElementById('jcBrandingStyle');
      if (!style) { style = document.createElement('style'); style.id = 'jcBrandingStyle'; document.head.appendChild(style); }
      style.textContent = css;
    });
  }

  function applyFavicon() {
    if (!branding.FaviconUrl) { return; }
    var links = document.querySelectorAll('link[rel~="icon"]');
    if (!links.length) { var l = document.createElement('link'); l.rel = 'icon'; document.head.appendChild(l); links = [l]; }
    for (var i = 0; i < links.length; i++) {
      if (links[i].getAttribute('href') !== branding.FaviconUrl) { links[i].setAttribute('href', branding.FaviconUrl); }
    }
  }

  function applyBrandLogo() {
    if (!branding.LogoUrl) { return; }
    var host = document.querySelector('.MuiToolbar-root') || document.querySelector('.skinHeader .headerLeft') || document.querySelector('.headerLeft');
    if (!host) { return; }
    var img = document.getElementById('jcBrandLogo');
    if (!img) {
      img = document.createElement('img');
      img.id = 'jcBrandLogo';
      img.className = 'jcBrandLogo';
      img.alt = '';
      img.addEventListener('click', function () { window.location.hash = '#/home'; });
    }
    if (img.getAttribute('src') !== branding.LogoUrl) { img.src = branding.LogoUrl; }
    // Sit the logo just to the RIGHT of the menu: after the drawer/menu button on the classic header
    // (10.11), else after the nav cluster on the Jellyfin 12 MUI toolbar. Falls back to the front. The
    // position check keeps this idempotent so the re-insert only fires when the client has moved it.
    var afterEl = host.querySelector('.mainDrawerButton') || host.querySelector('.MuiStack-root');
    var anchor = afterEl ? afterEl.nextSibling : host.firstChild;
    if (img.parentNode !== host || img.previousElementSibling !== afterEl) {
      host.insertBefore(img, anchor);
    }
  }

  function applyDrawerLinks() {
    var links = (branding && branding.DrawerLinks) || [];
    if (!links.length) { return; }
    var host = document.querySelector('.mainDrawer-scrollContainer') || document.querySelector('.navDrawerContent') || document.querySelector('.mainDrawer');
    if (!host) { return; }
    if (host.querySelector('.jcDrawerLink')) { return; } // already injected for this drawer render
    for (var i = 0; i < links.length; i++) {
      var link = links[i];
      if (!link || !link.Name) { continue; }
      var a = document.createElement('a');
      a.className = 'navMenuOption jcDrawerLink';
      a.setAttribute('is', 'emby-linkbutton');
      a.href = link.Url || '#';
      if (link.NewTab) { a.target = '_blank'; a.rel = 'noopener'; }
      var icon = document.createElement('span');
      icon.className = 'material-icons navMenuOptionIcon';
      icon.setAttribute('aria-hidden', 'true');
      icon.textContent = link.Icon || 'link';
      var text = document.createElement('span');
      text.className = 'navMenuOptionText';
      text.textContent = link.Name;
      a.appendChild(icon);
      a.appendChild(text);
      host.appendChild(a);
    }
  }

  function applyBranding() {
    if (!brandingEnabled()) { removeBranding(); return; }
    applyBrandingStyle();
    applyFavicon();
    applyBrandLogo();
    applyDrawerLinks();
  }

  function tryInsert() {
    if (!pluginVisible()) {
      removeHeaderStyle(); // restore the native header tabs for users who can't see the plugin
      return;
    }
    injectHeaderStyle();
    insertNav();
    insertQuota();
    insertBell();
    insertAnnouncement();
    insertHeaderLinks(); // after the announcement, so we can anchor the links just left of it
    installAvatarMenu(); // hijack the header avatar to open our dropdown instead of the prefs page
    insertMuiNav();      // Jellyfin 12 (MUI) toolbar — no-op on 10.11
    watchMuiToolbar();   // re-inject our MUI tabs when React re-renders the toolbar
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

  // Mirrors the server's MediaScope.Overlaps: do two season/episode scopes share content? (null = whole
  // series / whole season). Used to hide the claim button when a broader ownership already covers a season.
  function jcScopeOverlaps(sa, ea, sb, eb) {
    if (sa == null || sb == null) { return true; }
    if (sa !== sb) { return false; }
    return ea == null || eb == null || ea === eb;
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
        Title: item.title,
        Season: item.season != null ? item.season : null
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
    var uid = window.ApiClient.getCurrentUserId();

    // Runs the ownership check + injection for a resolved claim target { mediaType, tmdbId, season, title }.
    function proceed(target) {
      if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }

      function injectClaim() {
        if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
        var old = document.getElementById('jcDetailClaim');
        if (old && old.parentNode) { old.parentNode.removeChild(old); }
        if (!anchor.isConnected) { detailClaimPendingId = null; return; }
        anchor.appendChild(buildDetailClaim(target));
        detailClaimLoadedId = id;
      }

      // N32: only offer "Add to my library" if the user doesn't already own this scope (a whole-series
      // ownership also covers a season).
      window.ApiClient.ajax({ type: 'GET', url: getUrl('JellyCrowd/Requests/Mine'), dataType: 'json' })
        .then(function (mine) {
          if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
          var owned = (mine || []).some(function (r) {
            return r.TmdbId === target.tmdbId && r.MediaType === target.mediaType
              && (r.Status === 3 || r.Status === 'Available') && !r.DeletionRequestedAt
              && jcScopeOverlaps(target.season, null, r.Season, r.Episode);
          });
          if (owned) {
            removeDetailClaim();
            detailClaimLoadedId = id;
            return;
          }
          injectClaim();
        })
        .catch(injectClaim); // ownership check failed: show it anyway (the 409 still guards the claim).
    }

    window.ApiClient.getItem(uid, id)
      .then(function (it) {
        if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
        var type = it && it.Type;
        var tmdb = it && it.ProviderIds && (it.ProviderIds.Tmdb || it.ProviderIds.tmdb);
        if (type === 'Movie' && tmdb) {
          proceed({ mediaType: 'movie', tmdbId: parseInt(tmdb, 10), season: null, title: it.Name || '' });
        } else if (type === 'Series' && tmdb) {
          proceed({ mediaType: 'tv', tmdbId: parseInt(tmdb, 10), season: null, title: it.Name || '' });
        } else if (type === 'Season' && it.SeriesId && it.IndexNumber != null) {
          // A season carries no TMDB id of its own — it lives on the parent series. Fetch it, then claim
          // just this season (the requested item is the season, the ownership is scoped to it).
          window.ApiClient.getItem(uid, it.SeriesId)
            .then(function (series) {
              if (currentDetailItemId() !== id) { detailClaimPendingId = null; return; }
              var stmdb = series && series.ProviderIds && (series.ProviderIds.Tmdb || series.ProviderIds.tmdb);
              if (!stmdb) { detailClaimPendingId = null; return; }
              proceed({ mediaType: 'tv', tmdbId: parseInt(stmdb, 10), season: it.IndexNumber, title: series.Name || it.SeriesName || '' });
            })
            .catch(function () { detailClaimPendingId = null; });
        } else {
          detailClaimPendingId = null; // not a claimable page
        }
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
      applyBranding(); // re-assert branding when the web client re-renders (idempotent)
      if (overlay && overlay.style.display !== 'none') { positionOverlay(); }
      scheduleDetailInject();
    });
    observer.observe(document.body, { childList: true, subtree: true });
    tryInsert();
    applyBranding();
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

  // ---------- Local Intros (pre-roll) ----------
  // The plugin can play a pre-roll before content via Jellyfin's Cinema Mode. On the web client we
  // optionally force Cinema Mode on and make the pre-roll non-skippable: while a known pre-roll item plays
  // we hide the video OSD and swallow the seek/skip shortcuts. The client posts /Items/{id}/PlaybackInfo
  // and /Sessions/Playing* per queue item, so we watch those for a configured pre-roll item id.
  var jcIntroIds = null;      // Map of pre-roll item ids (dash-less, lowercase) -> 1
  var jcPreroll = false;

  function jcNormId(x) { return String(x || '').replace(/-/g, '').toLowerCase(); }

  // Shared playback-item detection. The client posts /Items/{id}/PlaybackInfo and /Sessions/Playing* per
  // queue item; we intercept both transports (XHR — ApiClient.ajax — and fetch) ONCE and fan the item id
  // out to every registered listener (the pre-roll guard, the Skip Outro control).
  var jcPlaybackListeners = [];
  var jcPlaybackWatchInstalled = false;

  function jcOnPlayback(cb) {
    jcPlaybackListeners.push(cb);
    jcInstallPlaybackWatch();
  }

  function jcFirePlayback(id) {
    for (var i = 0; i < jcPlaybackListeners.length; i++) {
      try { jcPlaybackListeners[i](id); } catch (e) { /* one listener must not break the others */ }
    }
  }

  function jcInstallPlaybackWatch() {
    if (jcPlaybackWatchInstalled) { return; }
    jcPlaybackWatchInstalled = true;
    var pbInfo = /\/Items\/([0-9a-fA-F-]{16,})\/PlaybackInfo/;
    function idFromUrl(u) { var m = pbInfo.exec(String(u || '')); return m ? m[1] : null; }
    function idFromBody(u, b) { if (!/\/Sessions\/Playing/.test(String(u || '')) || !b) { return null; } try { var j = JSON.parse(b); return j.ItemId || j.itemId || null; } catch (e) { return null; } }

    // A prototype hook is timing-safe regardless of when this runs.
    var XO = XMLHttpRequest.prototype.open, XS = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (m, u) { this._jcU = u; return XO.apply(this, arguments); };
    XMLHttpRequest.prototype.send = function (b) {
      try { var id = idFromUrl(this._jcU) || idFromBody(this._jcU, b); if (id) { jcFirePlayback(id); } } catch (e) { }
      return XS.apply(this, arguments);
    };
    var of = window.fetch;
    window.fetch = function (input, init) {
      try { var u = (typeof input === 'string') ? input : (input && input.url); var id = idFromUrl(u) || idFromBody(u, init && init.body); if (id) { jcFirePlayback(id); } } catch (e) { }
      return of.apply(this, arguments);
    };
  }

  function loadLocalIntros() {
    return fetch(getUrl('JellyCrowd/Settings/LocalIntros'))
      .then(function (r) { return r.json(); })
      .then(function (d) {
        if (!d || !d.Enabled) { return; }
        jcIntroIds = {};
        (d.ItemIds || []).forEach(function (x) { jcIntroIds[jcNormId(x)] = 1; });
        if (d.ForceCinemaMode) { jcForceCinemaMode(); }
        if (d.NonSkippable && (d.ItemIds || []).length) { jcInstallPrerollGuard(); }
      })
      .catch(function () { });
  }

  function jcForceCinemaMode() {
    try {
      var uid = window.ApiClient && window.ApiClient.getCurrentUserId && window.ApiClient.getCurrentUserId();
      if (uid) { localStorage.setItem(uid + '-enableCinemaMode', 'true'); }
    } catch (e) { /* best-effort */ }
  }

  function jcSetPreroll(on) {
    if (on === jcPreroll) { return; }
    jcPreroll = on;
    document.documentElement.classList.toggle('jc-preroll', on);
  }

  function jcOnPlaybackItem(id) {
    jcSetPreroll(!!(jcIntroIds && jcIntroIds[jcNormId(id)]));
  }

  function jcInstallPrerollGuard() {
    if (!document.getElementById('jc-preroll-css')) {
      var st = document.createElement('style'); st.id = 'jc-preroll-css';
      // Hide the video OSD (controls + seek bar + "skip to next") and the up-next prompt during a pre-roll.
      st.textContent = 'html.jc-preroll .videoOsdBottom,html.jc-preroll .osdControls,html.jc-preroll .upNextContainer,html.jc-preroll .skipIntro{display:none !important;visibility:hidden !important;}html.jc-preroll .videoPlayerContainer,html.jc-preroll .videoOsdBottom{cursor:none !important;}' +
        // During a non-skippable pre-roll the top bar must show ONLY the back arrow — hide the title and
        // every other header button (SyncPlay, Cast, search, user, home, drawer, and our own chrome), but
        // keep .headerBackButton so the user can still bail out.
        'html.jc-preroll .skinHeader .headerButton:not(.headerBackButton),html.jc-preroll .skinHeader .pageTitle,html.jc-preroll .skinHeader .headerTabs,html.jc-preroll .osdTitle{display:none !important;visibility:hidden !important;}';
      document.head.appendChild(st);
    }

    jcOnPlayback(jcOnPlaybackItem);

    // Swallow the seek / skip-to-next shortcuts while a pre-roll plays.
    document.addEventListener('keydown', function (e) {
      if (!jcPreroll) { return; }
      var k = e.key;
      var block = ['ArrowLeft', 'ArrowRight', 'MediaTrackNext', 'MediaTrackPrevious', 'PageUp', 'PageDown', 'Home', 'End'].indexOf(k) >= 0
        || /^[jJlL,.]$/.test(k)
        || (e.shiftKey && /^[nNpPbBfF]$/.test(k));
      if (block) { e.stopImmediatePropagation(); e.preventDefault(); }
    }, true);
  }

  // ---------- Skip Outro (smart) ----------
  // Native Jellyfin just seeks past an outro segment. We do more, driven by the plugin's own end-credits
  // data (the fingerprinted ED first, then the brightness/silence heuristic) and decided by the tested
  // outroSkipPlan: when the credits run to the END of the episode, advance to the NEXT one automatically
  // after a short countdown; when a post-credits bonus remains, offer to jump to it and never auto-skip.
  var jcOutro = null;         // { OutroStartTicks, OutroEndTicks, RunTimeTicks } for the current item
  var jcOutroTick = null;     // poll interval reading the video position
  var jcOutroBtn = null;      // the button element (null when hidden)
  var jcOutroDeadline = 0;    // epoch ms at which the 'next' auto-skip fires; 0 = no countdown running

  function jcInstallOutroSkip() {
    // The decision helper lives in catalog.lib.js, which the catalog page loads lazily — pull it in now so
    // it is ready by the time an outro plays.
    if (!window.JellyCrowdLib) {
      var lib = document.createElement('script');
      lib.src = getUrl('JellyCrowd/Web/catalog.lib.js');
      document.head.appendChild(lib);
    }
    if (!document.getElementById('jc-outro-css')) {
      var st = document.createElement('style'); st.id = 'jc-outro-css';
      st.textContent =
        '.jc-outro-btn{position:fixed;right:5%;bottom:13%;z-index:1000;padding:.7em 1.15em;border:0;border-radius:.4em;'
        + 'background:rgba(0,0,0,.72);color:#fff;font:600 1.05em/1 inherit;cursor:pointer;'
        + 'box-shadow:0 2px 14px rgba(0,0,0,.55);}'
        + '.jc-outro-btn:hover,.jc-outro-btn:focus-visible{background:' + NAV_BLUE + ';outline:none;}'
        // While our control shows, hide the native segment skip button so there is only one (the intro is
        // long over by the outro, so hiding .skipIntro here is safe).
        + 'html.jc-outro-on .skipIntro{display:none !important;}';
      document.head.appendChild(st);
    }
    jcOnPlayback(jcOutroLoad);
  }

  function jcOutroLoad(id) {
    jcOutroReset();
    apiAjax('GET', 'JellyCrowd/Playback/Outro/' + id)
      .then(function (d) {
        if (d && d.OutroEndTicks > d.OutroStartTicks && d.RunTimeTicks > 0) {
          jcOutro = d;
          jcOutroStopWatch();
          jcOutroTick = setInterval(jcOutroPoll, 400);
        }
      })
      .catch(function () { /* 204 (no outro for this item) or offline — nothing to do */ });
  }

  function jcVideo() { return document.querySelector('video'); }
  function jcOutroStopWatch() { if (jcOutroTick) { clearInterval(jcOutroTick); jcOutroTick = null; } }

  function jcOutroPoll() {
    var v = jcVideo();
    var lib = window.JellyCrowdLib;
    if (!v || !jcOutro || !lib || !lib.outroSkipPlan) { return; }

    var TPS = 10000000;
    var startS = jcOutro.OutroStartTicks / TPS;
    var endS = jcOutro.OutroEndTicks / TPS;
    var t = v.currentTime || 0;
    if (t < startS || t >= endS - 0.4) { jcOutroHide(); return; } // outside the credits: no control

    var plan = lib.outroSkipPlan(jcOutro.OutroStartTicks, jcOutro.OutroEndTicks, jcOutro.RunTimeTicks);
    if (plan) { jcOutroShow(v, plan); }
  }

  function jcOutroShow(v, plan) {
    if (!jcOutroBtn) {
      jcOutroBtn = document.createElement('button');
      jcOutroBtn.type = 'button';
      jcOutroBtn.className = 'jc-outro-btn';
      jcOutroBtn.addEventListener('click', function () { jcOutroAct(jcOutroBtn && jcOutroBtn._plan); });
      document.body.appendChild(jcOutroBtn);
      document.documentElement.classList.add('jc-outro-on');
      jcOutroBtn._mode = null;
    }
    jcOutroBtn._plan = plan;

    if (plan.mode === 'bonus') {
      jcOutroDeadline = 0;
      jcOutroBtn.textContent = '⏭ ' + t('outro_skip_bonus');
      return;
    }

    // 'next': auto-skip after a countdown — but only while actually playing. A paused viewer keeps the
    // full countdown, so pausing during the credits never yanks them into the next episode.
    if (v.paused || jcOutroBtn._mode !== 'next') {
      jcOutroDeadline = Date.now() + (plan.autoSkipSeconds * 1000);
    }
    jcOutroBtn._mode = 'next';
    var remaining = Math.max(0, Math.ceil((jcOutroDeadline - Date.now()) / 1000));
    jcOutroBtn.textContent = '⏭ ' + t('outro_next_episode') + ' (' + remaining + ')';
    if (remaining <= 0) { jcOutroAct(plan); }
  }

  function jcOutroAct(plan) {
    if (!plan) { return; }
    var v = jcVideo();
    if (v) {
      // 'next': seek to the very end (the credits ARE the end) so the item finishes and Jellyfin plays the
      // next one; 'bonus': land the viewer exactly where the credits end.
      var target = plan.mode === 'next'
        ? ((isFinite(v.duration) && v.duration > 0) ? v.duration : plan.seekSeconds)
        : plan.seekSeconds;
      try { v.currentTime = target; } catch (e) { /* some players clamp — best-effort */ }
    }
    jcOutroHide();
  }

  function jcOutroHide() {
    jcOutroDeadline = 0;
    if (jcOutroBtn) {
      jcOutroBtn.remove();
      jcOutroBtn = null;
      document.documentElement.classList.remove('jc-outro-on');
    }
  }

  function jcOutroReset() {
    jcOutro = null;
    jcOutroStopWatch();
    jcOutroHide();
  }

  loadConfigLang().then(loadStrings).then(loadBranding).then(start).then(resolveAdminVisibility)
    .then(loadLocalIntros)
    .then(function () { if (jcSkipOutro) { jcInstallOutroSkip(); } });
})();
