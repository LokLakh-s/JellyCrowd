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
    { id: 'mymedia', file: 'mymedia.html', labelKey: 'my_media_title' }
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
  var bellBadgeEl = null;          // the red unread-count badge on the header bell
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
  var announcement = { text: '', level: 'green' };

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
        if (d) { announcement = { text: d.AnnouncementText || '', level: d.AnnouncementLevel || 'green' }; }
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
        tryInsert(); // (re-)render now that admin state is known (banner edit affordance, etc.)
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

    overlay.style.display = '';
    positionOverlay();
    setActiveNav(id);
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

  function buildQuota() {
    var box = document.createElement('span');
    box.style.cssText = 'display:inline-flex;flex-direction:column;justify-content:center;min-width:8em;margin:0 .6em;font-size:.7em;cursor:pointer;';
    box.title = t('my_media_title');
    box.addEventListener('click', function () { toggleView('mymedia'); });
    var caption = document.createElement('span');
    caption.textContent = t('my_media_title');
    caption.style.cssText = 'color:#fff;font-weight:600;opacity:.85;';
    var label = document.createElement('span');
    label.style.color = '#fff';
    var track = document.createElement('span');
    track.style.cssText = 'height:.35em;border-radius:.2em;background:rgba(255,255,255,.2);overflow:hidden;display:block;margin-top:.2em;';
    var fill = document.createElement('span');
    fill.style.cssText = 'display:block;height:100%;background:' + quotaColor(0) + ';width:0%;';
    track.appendChild(fill);
    box.appendChild(caption);
    box.appendChild(label);
    box.appendChild(track);

    if (window.ApiClient && window.ApiClient.ajax) {
      window.ApiClient.ajax({ type: 'GET', url: getUrl('JellyCrowd/Quota/Me'), dataType: 'json' })
        .then(function (q) {
          if (!q) { return; }
          if (q.Unlimited || q.QuotaBytes <= 0) {
            label.textContent = t('quota_storage') + ': ' + bytes(q.UsedBytes) + ' / ' + t('quota_unlimited');
            track.style.display = 'none';
          } else {
            label.textContent = bytes(q.UsedBytes) + ' / ' + bytes(q.QuotaBytes);
            var p = q.QuotaBytes > 0 ? Math.min(100, q.UsedBytes / q.QuotaBytes * 100) : 0;
            fill.style.width = p + '%';
            fill.style.background = quotaColor(p);
          }
        })
        .catch(function () { /* ignore */ });
    }

    return box;
  }

  // Catalog / My requests render as extra tabs right next to Jellyfin's own Home / Favorites, inside
  // the centered .headerTabs row. That row is page-specific (shown on Home / library pages, hidden on
  // detail / search / settings), so these links follow the same visibility — by design. Jellyfin
  // rebuilds the tab bar on navigation, so the MutationObserver re-inserts us whenever it's wiped.
  function insertNav() {
    if (document.querySelector('.jcHeaderNav')) {
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
    nav.appendChild(navButton('nav_catalog', 'catalog'));
    nav.appendChild(navButton('nav_calendar', 'calendar'));
    nav.appendChild(navButton('nav_requests', 'requests'));
    // Sit on the same line as the real tabs when the slider exists, else in the row/host itself.
    var slider = tabs ? tabs.querySelector('.emby-tabs-slider') : null;
    (slider || host).appendChild(nav);
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
    wrap.style.cssText = 'display:inline-flex;align-items:center;';
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
            NtfyTopic: ntfyInp.value.trim()
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
    wrap.style.cssText = 'position:relative;display:inline-flex;align-items:center;';

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
      panel.style.right = Math.round(window.innerWidth - r.right) + 'px';
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

  function renderAnnouncementInner(box) {
    box.innerHTML = '';
    var hasText = !!(announcement.text && announcement.text.trim());
    if (!hasText && !isAdmin) { box.style.display = 'none'; return; }
    box.style.display = 'inline-flex';
    if (hasText) {
      var c = announcementColors(announcement.level);
      box.style.background = c.bg;
      box.style.color = c.fg;
      box.style.border = '0';
      var txt = document.createElement('span');
      txt.style.cssText = 'overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
      txt.textContent = announcement.text;
      txt.title = announcement.text;
      box.appendChild(txt);
    } else {
      box.style.background = 'transparent';
      box.style.color = 'inherit';
      box.style.border = '1px dashed rgba(255,255,255,.4)';
      var add = document.createElement('span');
      add.textContent = t('announcement_add');
      add.style.opacity = '.8';
      box.appendChild(add);
    }
    if (isAdmin) {
      var edit = document.createElement('button');
      edit.type = 'button';
      edit.textContent = '✎';
      edit.title = t('announcement_edit');
      edit.style.cssText = 'margin-left:.4em;background:none;border:0;color:inherit;cursor:pointer;font-size:1em;flex:0 0 auto;';
      edit.addEventListener('click', function (e) { e.stopPropagation(); openAnnouncementEditor(); });
      box.appendChild(edit);
    }
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
    function save(text, level) {
      apiAjax('POST', 'JellyCrowd/Settings/Announcement', { Text: text, Level: level })
        .then(function () { announcement = { text: (text || '').trim(), level: level }; pop.remove(); refreshAnnouncement(); })
        .catch(function () { /* ignore */ });
    }
    var clearBtn = document.createElement('button');
    clearBtn.type = 'button'; clearBtn.textContent = t('announcement_clear');
    clearBtn.style.cssText = 'background:none;border:1px solid rgba(255,255,255,.3);color:#fff;border-radius:.3em;padding:.3em .7em;cursor:pointer;';
    clearBtn.addEventListener('click', function () { save('', sel.value); });
    var saveBtn = document.createElement('button');
    saveBtn.type = 'button'; saveBtn.textContent = t('save');
    saveBtn.style.cssText = 'background:#00a4dc;border:0;color:#fff;border-radius:.3em;padding:.3em .8em;cursor:pointer;';
    saveBtn.addEventListener('click', function () { save(ta.value, sel.value); });
    actions.appendChild(clearBtn); actions.appendChild(saveBtn);
    pop.appendChild(ta); pop.appendChild(sel); pop.appendChild(actions);
    pop.addEventListener('click', function (e) { e.stopPropagation(); });
    document.body.appendChild(pop);
    var onDoc = function () { pop.remove(); document.removeEventListener('click', onDoc); };
    setTimeout(function () { document.addEventListener('click', onDoc); }, 0);
  }

  function refreshAnnouncement() {
    var box = document.querySelector('.jcHeaderAnnounce');
    if (box) { renderAnnouncementInner(box); }
  }

  // Announcement banner sits in the header's left area, just after the logo/home button.
  function insertAnnouncement() {
    var host = document.querySelector('.skinHeader .headerLeft') || document.querySelector('.headerLeft');
    if (!host) { return; }
    var box = document.querySelector('.jcHeaderAnnounce');
    if (!box) {
      box = document.createElement('span');
      box.className = 'jcHeaderAnnounce';
      box.style.cssText = 'display:inline-flex;align-items:center;gap:.3em;margin:0 .8em;padding:.15em .7em;border-radius:.4em;font-size:.82em;font-weight:600;max-width:40vw;overflow:hidden;';
      host.appendChild(box);
    }
    renderAnnouncementInner(box);
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
      '.headerTabs:has(.jcHeaderNav){display:flex !important;justify-content:center;}';
    document.head.appendChild(style);
  }

  function tryInsert() {
    if (!pluginVisible()) {
      return;
    }
    insertNav();
    insertQuota();
    insertBell();
    insertAnnouncement();
  }

  function start() {
    injectHeaderStyle();
    var observer = new MutationObserver(function () {
      tryInsert();
      if (overlay && overlay.style.display !== 'none') { positionOverlay(); }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    tryInsert();
    setInterval(refreshBellBadge, 30000);
    // Any real navigation (Jellyfin menu, opening a library item) closes our overlay.
    window.addEventListener('hashchange', hideOverlay);
  }

  loadConfigLang().then(loadStrings).then(start).then(resolveAdminVisibility);
})();
