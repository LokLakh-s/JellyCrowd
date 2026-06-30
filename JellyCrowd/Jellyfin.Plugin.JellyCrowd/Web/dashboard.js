/*
 * Jelly Crowd — "Dashboard" page. A personal profile for the current user: their Jellyfin viewing stats
 * (watch time, plays, top movies/shows, recent activity), their request activity, and their disk quota.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';
  var windowDays = 30;

  function shortLang() { return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US'); }
  function t(key) { return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key; }

  function pluginUrl(path) {
    return (window.ApiClient && typeof window.ApiClient.getUrl === 'function') ? window.ApiClient.getUrl(path) : '/' + path;
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
      .catch(function () { /* keep 'auto' */ });
  }

  function loadStrings() {
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
  }

  function setMessage(text) {
    var el = document.getElementById('jcDashMessage');
    if (!el) { return; }
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  function hours(min) { return Math.round((min || 0) / 60); }

  function statCard(value, label) {
    var c = document.createElement('div');
    c.className = 'jellycrowd-stat-card';
    var v = document.createElement('div');
    v.className = 'jellycrowd-stat-value';
    v.textContent = value;
    var l = document.createElement('div');
    l.className = 'jellycrowd-stat-label';
    l.textContent = label;
    c.appendChild(v);
    c.appendChild(l);
    return c;
  }

  function heading(text) {
    var h = document.createElement('h3');
    h.className = 'jellycrowd-branding-heading';
    h.textContent = text;
    return h;
  }

  function rankTable(title, rows) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-stat-section';
    section.appendChild(heading(title));
    if (!rows || !rows.length) {
      var empty = document.createElement('p');
      empty.className = 'jellycrowd-field-hint';
      empty.textContent = t('stats_empty');
      section.appendChild(empty);
      return section;
    }
    var table = document.createElement('table');
    table.className = 'jellycrowd-admin-table';
    var tbody = document.createElement('tbody');
    rows.forEach(function (r, i) {
      var tr = document.createElement('tr');
      function td(text, cls) { var c = document.createElement('td'); c.textContent = text; if (cls) { c.className = cls; } return c; }
      tr.appendChild(td('#' + (i + 1), 'jellycrowd-admin-sub'));
      tr.appendChild(td(r.Name || '—'));
      tr.appendChild(td(r.Plays + ' ' + t('stats_plays_unit'), 'jellycrowd-admin-sub'));
      tr.appendChild(td(hours(r.Minutes) + ' h', 'jellycrowd-admin-sub'));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    section.appendChild(table);
    return section;
  }

  function recentTable(rows) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-stat-section';
    section.appendChild(heading(t('stats_recent')));
    if (!rows || !rows.length) {
      var empty = document.createElement('p');
      empty.className = 'jellycrowd-field-hint';
      empty.textContent = t('stats_empty');
      section.appendChild(empty);
      return section;
    }
    var table = document.createElement('table');
    table.className = 'jellycrowd-admin-table';
    var tbody = document.createElement('tbody');
    rows.forEach(function (r) {
      var tr = document.createElement('tr');
      function td(text, cls) { var c = document.createElement('td'); c.textContent = text; if (cls) { c.className = cls; } return c; }
      tr.appendChild(td(r.Label || '—'));
      var when = r.PlayedAtUtc ? new Date(r.PlayedAtUtc).toLocaleString() : '';
      tr.appendChild(td(when, 'jellycrowd-admin-sub'));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    section.appendChild(table);
    return section;
  }

  function quotaBar(d) {
    var wrap = document.createElement('div');
    wrap.className = 'jellycrowd-dash-quota';
    if (d.QuotaUnlimited || !d.QuotaTotalBytes) {
      var unlimited = document.createElement('div');
      unlimited.className = 'jellycrowd-field-hint';
      unlimited.textContent = lib.formatBytes(d.QuotaUsedBytes) + ' · ' + t('dashboard_quota_unlimited');
      wrap.appendChild(unlimited);
      return wrap;
    }
    var pct = lib.quotaPercent(d.QuotaUsedBytes, d.QuotaTotalBytes);
    var label = document.createElement('div');
    label.className = 'jellycrowd-field-hint';
    label.textContent = lib.formatBytes(d.QuotaUsedBytes) + ' / ' + lib.formatBytes(d.QuotaTotalBytes) + ' (' + pct + '%)';
    var track = document.createElement('div');
    track.className = 'jellycrowd-dash-quota-track';
    var fill = document.createElement('div');
    fill.className = 'jellycrowd-dash-quota-fill';
    fill.style.width = pct + '%';
    fill.style.background = lib.quotaColor(pct);
    track.appendChild(fill);
    wrap.appendChild(label);
    wrap.appendChild(track);
    return wrap;
  }

  function periodBar(reload) {
    var period = document.createElement('div');
    period.className = 'jellycrowd-stats-period';
    [[7, '7 j'], [30, '30 j'], [90, '90 j'], [365, '1 an'], [0, t('stats_all')]].forEach(function (p) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jellycrowd-admin-tab' + (windowDays === p[0] ? ' jellycrowd-admin-tab-active' : '');
      b.textContent = p[1];
      b.addEventListener('click', function () { windowDays = p[0]; reload(); });
      period.appendChild(b);
    });
    return period;
  }

  // Compact "My storage" widget that sits beside the period selector instead of at the page bottom.
  function storageWidget(d) {
    var box = document.createElement('div');
    box.className = 'jellycrowd-dash-storage';
    var title = document.createElement('div');
    title.className = 'jellycrowd-dash-storage-title';
    title.textContent = t('dashboard_quota');
    box.appendChild(title);
    box.appendChild(quotaBar(d));
    return box;
  }

  function render(d) {
    var content = document.getElementById('jcDashContent');
    if (!content) { return; }
    content.innerHTML = '';
    d = d || {};

    // Top row: period selector (+ "recording since" caption) on the left, storage on the right.
    var topRow = document.createElement('div');
    topRow.className = 'jellycrowd-dash-top';
    var topLeft = document.createElement('div');
    topLeft.appendChild(periodBar(load));
    if (d.DataSinceUtc) {
      var since = document.createElement('div');
      since.className = 'jellycrowd-dash-since';
      since.textContent = t('dashboard_data_since') + ' ' + new Date(d.DataSinceUtc).toLocaleDateString();
      topLeft.appendChild(since);
    }
    topRow.appendChild(topLeft);
    topRow.appendChild(storageWidget(d));
    content.appendChild(topRow);

    // Viewing
    content.appendChild(heading(t('dashboard_viewing')));
    var viewCards = document.createElement('div');
    viewCards.className = 'jellycrowd-stat-cards';
    viewCards.appendChild(statCard(hours(d.TotalMinutes) + ' h', t('stats_watchtime')));
    viewCards.appendChild(statCard(d.TotalPlays || 0, t('stats_plays')));
    content.appendChild(viewCards);

    var grid = document.createElement('div');
    grid.className = 'jellycrowd-stat-grid';
    grid.appendChild(rankTable(t('stats_top_movies'), d.TopMovies));
    grid.appendChild(rankTable(t('stats_top_shows'), d.TopShows));
    content.appendChild(grid);
    content.appendChild(recentTable(d.Recent));

    // Requests
    content.appendChild(heading(t('dashboard_requests')));
    var reqCards = document.createElement('div');
    reqCards.className = 'jellycrowd-stat-cards';
    reqCards.appendChild(statCard(d.RequestsTotal || 0, t('dashboard_req_total')));
    reqCards.appendChild(statCard(d.RequestsPending || 0, t('dashboard_req_pending')));
    reqCards.appendChild(statCard(d.RequestsApproved || 0, t('dashboard_req_progress')));
    reqCards.appendChild(statCard(d.RequestsAvailable || 0, t('dashboard_req_available')));
    reqCards.appendChild(statCard(d.RequestsDenied || 0, t('dashboard_req_denied')));
    content.appendChild(reqCards);
  }

  function load() {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Stats/Me?windowDays=' + windowDays)
      .then(function (d) { setMessage(''); render(d); })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      var logo = document.getElementById('jcDashLogo');
      if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
      var title = document.getElementById('jcDashTitle');
      if (title) { title.textContent = t('dashboard_title'); }
      load();
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('dashboard', load);
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
