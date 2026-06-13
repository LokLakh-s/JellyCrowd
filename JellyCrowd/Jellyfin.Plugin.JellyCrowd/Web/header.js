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

  // The user pages we host. Order defines the overlay tab order.
  var VIEWS = [
    { id: 'catalog', file: 'catalog.html', labelKey: 'nav_catalog' },
    { id: 'requests', file: 'requests.html', labelKey: 'nav_requests' },
    { id: 'mymedia', file: 'mymedia.html', labelKey: 'my_media_title' }
  ];

  var overlay = null;
  var viewHost = null;
  // Per-view refresh callbacks, registered by the hosted pages (see window.jellyCrowdRegisterRefresh).
  // Called every time an already-loaded view is shown again, so e.g. "My requests" picks up a
  // request just made from the catalog without a full page reload.
  var viewRefreshers = {};

  function getUrl(p) {
    return (window.ApiClient && window.ApiClient.getUrl) ? window.ApiClient.getUrl(p) : '/' + p;
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

  function loadConfigLang() {
    return fetch(getUrl('JellyCrowd/Settings/Language'))
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) { if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); } })
      .catch(function () { /* keep 'auto' on failure */ });
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

  function ensureOverlay() {
    if (overlay) {
      return;
    }
    overlay = document.createElement('div');
    overlay.className = 'jellycrowd-overlay';
    overlay.style.display = 'none';

    var bar = document.createElement('div');
    bar.className = 'jellycrowd-overlay-bar';

    var tabs = document.createElement('div');
    tabs.className = 'jellycrowd-overlay-tabs';
    VIEWS.forEach(function (v) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jellycrowd-overlay-tab';
      b.textContent = t(v.labelKey);
      b.addEventListener('click', function () { showView(v.id); });
      v.tab = b;
      tabs.appendChild(b);
    });

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'jellycrowd-overlay-close';
    close.setAttribute('aria-label', t('close'));
    close.textContent = '✕';
    close.addEventListener('click', hideOverlay);

    bar.appendChild(tabs);
    bar.appendChild(close);

    viewHost = document.createElement('div');
    viewHost.className = 'jellycrowd-overlay-views';

    overlay.appendChild(bar);
    overlay.appendChild(viewHost);
    document.body.appendChild(overlay);

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
    VIEWS.forEach(function (v) {
      if (v.container) {
        v.container.style.display = (v.id === id) ? '' : 'none';
      }
      if (v.tab) {
        v.tab.classList.toggle('jellycrowd-overlay-tab-active', v.id === id);
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
  }

  // Let our pages (e.g. the requests quota bar) switch views without touching the URL hash.
  window.jellyCrowdShowView = showView;

  // Let a hosted page register a callback re-run each time its (already-loaded) view is shown again.
  window.jellyCrowdRegisterRefresh = function (id, fn) { viewRefreshers[id] = fn; };

  // ---------- header injection ----------

  function navButton(labelKey, viewId) {
    var a = document.createElement('button');
    a.type = 'button';
    a.textContent = t(labelKey);
    // Match the look of Jellyfin's own header links (Home / Favorites): white text, same size,
    // vertically centered. The header theme varies, so colours are forced rather than inherited.
    a.style.cssText = 'margin:0;padding:0 .8em;color:#fff;background:none;border:none;font-size:1em;font-weight:400;line-height:1;cursor:pointer;align-self:center;white-space:nowrap;';
    a.addEventListener('mouseenter', function () { a.style.opacity = '.7'; });
    a.addEventListener('mouseleave', function () { a.style.opacity = '1'; });
    a.addEventListener('click', function () { showView(viewId); });
    return a;
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
    box.addEventListener('click', function () { showView('mymedia'); });
    var label = document.createElement('span');
    var track = document.createElement('span');
    track.style.cssText = 'height:.35em;border-radius:.2em;background:rgba(255,255,255,.2);overflow:hidden;display:block;margin-top:.2em;';
    var fill = document.createElement('span');
    fill.style.cssText = 'display:block;height:100%;background:#00a4dc;width:0%;';
    track.appendChild(fill);
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
          }
        })
        .catch(function () { /* ignore */ });
    }

    return box;
  }

  function insert(host) {
    if (!host || document.querySelector('.jcHeaderBtns')) {
      return;
    }
    var wrap = document.createElement('span');
    wrap.className = 'jcHeaderBtns';
    wrap.style.cssText = 'display:inline-flex;align-items:center;';
    wrap.appendChild(navButton('nav_catalog', 'catalog'));
    wrap.appendChild(navButton('nav_requests', 'requests'));
    wrap.appendChild(buildQuota());
    host.insertBefore(wrap, host.firstChild);
  }

  function tryInsert() {
    insert(document.querySelector('.headerRight'));
  }

  function start() {
    var observer = new MutationObserver(function () { tryInsert(); });
    observer.observe(document.body, { childList: true, subtree: true });
    tryInsert();
    // Any real navigation (Jellyfin menu, opening a library item) closes our overlay.
    window.addEventListener('hashchange', hideOverlay);
  }

  loadConfigLang().then(loadStrings).then(start);
})();
