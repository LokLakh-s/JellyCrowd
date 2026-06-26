/*
 * Jelly Crowd — "Ownership" page (admin only). Shows who currently owns which media (every available
 * title, grouped by its exact scope, with the owning users). Reuses pure helpers from catalog.lib.js;
 * strings follow the Jellyfin/browser language. The backend endpoint is admin-protected; this view is
 * also only shown to admins by header.js.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w92';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';
  var items = [];          // last-loaded ownership list (for client-side filtering)

  function shortLang() {
    return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US');
  }

  function t(key) {
    return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
  }

  function pluginUrl(path) {
    if (window.ApiClient && typeof window.ApiClient.getUrl === 'function') {
      return window.ApiClient.getUrl(path);
    }
    return '/' + path;
  }

  function apiGet(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'GET', url: pluginUrl(path), dataType: 'json' });
    }
    return fetch(pluginUrl(path)).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
      return r.json();
    });
  }

  function loadConfigLang() {
    return apiGet('JellyCrowd/Settings/Language')
      .then(function (d) { if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); } })
      .catch(function () { /* keep 'auto' on failure */ });
  }

  function loadStrings() {
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
  }

  function setMessage(text) {
    var el = document.getElementById('jcOwnMessage');
    if (!el) { return; }
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  // "S3", "S2E5" or "" (movie / whole title).
  function scopeLabel(item) {
    if (item.Season == null) { return ''; }
    return 'S' + item.Season + (item.Episode != null ? 'E' + item.Episode : '');
  }

  function renderRow(item, listEl) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-own-row';

    if (item.PosterPath) {
      var img = document.createElement('img');
      img.className = 'jellycrowd-own-poster';
      img.loading = 'lazy';
      img.alt = '';
      img.src = POSTER_BASE + item.PosterPath;
      row.appendChild(img);
    }

    var body = document.createElement('div');
    body.className = 'jellycrowd-own-body';

    var head = document.createElement('div');
    head.className = 'jellycrowd-own-head';
    var title = document.createElement('a');
    title.href = 'https://www.themoviedb.org/' + item.MediaType + '/' + item.TmdbId;
    title.target = '_blank';
    title.rel = 'noopener noreferrer';
    title.className = 'jellycrowd-link jellycrowd-own-title';
    title.textContent = item.Title || ((item.MediaType === 'tv' ? t('media_type_tv') : t('media_type_movie')) + ' #' + item.TmdbId);
    head.appendChild(title);
    var scope = scopeLabel(item);
    if (scope) {
      var badge = document.createElement('span');
      badge.className = 'jellycrowd-own-scope';
      badge.textContent = scope;
      head.appendChild(badge);
    }
    var count = document.createElement('span');
    count.className = 'jellycrowd-own-count';
    count.textContent = (item.Owners ? item.Owners.length : 0) + ' ' + t('ownership_owners');
    head.appendChild(count);
    body.appendChild(head);

    var owners = document.createElement('div');
    owners.className = 'jellycrowd-own-owners';
    (item.Owners || []).forEach(function (o) {
      var chip = document.createElement('span');
      chip.className = 'jellycrowd-own-owner';
      chip.textContent = o.Name;
      if (o.SinceUtc) {
        chip.title = t('ownership_since') + ' ' + new Date(o.SinceUtc).toLocaleDateString();
      }
      owners.appendChild(chip);
    });
    body.appendChild(owners);

    row.appendChild(body);
    listEl.appendChild(row);
  }

  function render() {
    var listEl = document.getElementById('jcOwnList');
    if (!listEl) { return; }
    var q = (document.getElementById('jcOwnFilter').value || '').trim().toLowerCase();
    var shown = items.filter(function (it) {
      if (!q) { return true; }
      if ((it.Title || '').toLowerCase().indexOf(q) >= 0) { return true; }
      return (it.Owners || []).some(function (o) { return (o.Name || '').toLowerCase().indexOf(q) >= 0; });
    });
    listEl.innerHTML = '';
    if (!shown.length) { setMessage(q ? t('no_results') : t('ownership_empty')); return; }
    setMessage('');
    shown.forEach(function (it) { renderRow(it, listEl); });
  }

  function load() {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Requests/Ownerships')
      .then(function (data) { items = data || []; render(); })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      var logo = document.getElementById('jcOwnLogo');
      if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
      document.getElementById('jcOwnTitle').textContent = t('nav_ownership');
      document.getElementById('jcOwnSubtitle').textContent = t('ownership_subtitle');
      var filter = document.getElementById('jcOwnFilter');
      filter.placeholder = t('ownership_filter_placeholder');
      filter.addEventListener('input', render);
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('ownership', load);
      }
      load();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
