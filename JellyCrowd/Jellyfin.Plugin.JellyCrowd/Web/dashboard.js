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
  var chartMetric = 'minutes';
  var activeTab = 'overview';               // 'overview' | 'history'
  var historyFilters = { query: '', from: '', to: '' };

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

  function apiPost(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path) });
    }
    return fetch(pluginUrl(path), { method: 'POST' }).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
      return r;
    });
  }

  // Confirmation in our own dialog (no window.confirm): resolves true only on confirm.
  function confirmAction(opts) {
    return new Promise(function (resolve) {
      var d = lib.buildConfirmDialog(document, {
        title: opts.title, message: opts.message, confirmLabel: opts.confirmLabel, cancelLabel: t('cancel'), danger: true
      });
      var opener = document.activeElement;
      function close(result) {
        document.removeEventListener('keydown', onKey, true);
        if (d.root.parentNode) { d.root.parentNode.removeChild(d.root); }
        var back = lib.focusRestoreTarget(opener, document);
        if (back) { back.focus(); }
        resolve(result);
      }
      function onKey(e) {
        if (e.key === 'Escape') { e.preventDefault(); close(false); return; }
        if (e.key === 'Tab') { lib.handleTrapKeydown(e, d.root); }
      }
      d.cancel.addEventListener('click', function () { close(false); });
      d.confirm.addEventListener('click', function () { close(true); });
      d.root.addEventListener('click', function (e) { if (e.target === d.root) { close(false); } });
      document.addEventListener('keydown', onKey, true);
      document.body.appendChild(d.root);
      lib.focusFirst(d.root);
    });
  }

  function loadConfigLang() {
    return apiGet('JellyCrowd/Settings/Language')
      .then(function (d) { if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); } })
      .catch(function () { /* keep 'auto' */ });
  }

  function loadStrings() {
    // Keep the labels already in hand when the catalog cannot be read: replacing them with an empty
    // object turns every button in the view into its raw key.
    return lib.fetchStrings(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (loaded) { if (loaded) { strings = loaded; } });
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

  // SVG bar chart of the daily series for the currently-selected metric (watch time or plays).
  function dashChart(daily) {
    var NS = 'http://www.w3.org/2000/svg';
    var pick = function (d) { return chartMetric === 'plays' ? d.Plays : d.Minutes; };
    var max = 1;
    daily.forEach(function (d) { var v = pick(d); if (v > max) { max = v; } });
    var n = daily.length, bw = 100 / n;
    var svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 100 32');
    svg.setAttribute('preserveAspectRatio', 'none');
    svg.setAttribute('class', 'jellycrowd-chart');
    daily.forEach(function (d, i) {
      var v = pick(d);
      var h = (v / max) * 30;
      var rect = document.createElementNS(NS, 'rect');
      rect.setAttribute('x', String(i * bw + bw * 0.12));
      rect.setAttribute('y', String(30 - (v > 0 ? Math.max(h, 0.6) : 0)));
      rect.setAttribute('width', String(bw * 0.76));
      rect.setAttribute('height', String(v > 0 ? Math.max(h, 0.6) : 0));
      rect.setAttribute('class', 'jellycrowd-chart-bar');
      var title = document.createElementNS(NS, 'title');
      title.textContent = d.Date + ': ' + hours(d.Minutes) + ' h · ' + d.Plays + ' ×';
      rect.appendChild(title);
      svg.appendChild(rect);
    });
    return svg;
  }

  // Activity section with a metric toggle (watch time / plays). The toggle re-renders the chart
  // client-side from the same data — no server round-trip.
  function chartSection(daily) {
    var section = document.createElement('div');
    section.appendChild(heading(t('dashboard_activity')));
    var toggle = document.createElement('div');
    toggle.className = 'jellycrowd-stats-period';
    var host = document.createElement('div');
    [['minutes', t('stats_watchtime')], ['plays', t('stats_plays')]].forEach(function (m) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = m[1];
      b.className = 'jellycrowd-admin-tab' + (chartMetric === m[0] ? ' jellycrowd-admin-tab-active' : '');
      b.addEventListener('click', function () {
        chartMetric = m[0];
        [].forEach.call(toggle.children, function (c) { c.className = 'jellycrowd-admin-tab'; });
        b.className = 'jellycrowd-admin-tab jellycrowd-admin-tab-active';
        host.innerHTML = '';
        host.appendChild(dashChart(daily));
      });
      toggle.appendChild(b);
    });
    section.appendChild(toggle);
    host.appendChild(dashChart(daily));
    section.appendChild(host);
    return section;
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
    [[7, t('stats_period_7d')], [30, t('stats_period_30d')], [90, t('stats_period_90d')], [365, t('stats_period_1y')], [0, t('stats_all')]].forEach(function (p) {
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

  // Watch time split by library (top libraries by minutes).
  function libraryTable(rows) {
    var section = document.createElement('div');
    section.appendChild(heading(t('dashboard_by_library')));
    var table = document.createElement('table');
    table.className = 'jellycrowd-admin-table';
    var tbody = document.createElement('tbody');
    rows.forEach(function (r) {
      var tr = document.createElement('tr');
      function td(x, cls) { var c = document.createElement('td'); c.textContent = x; if (cls) { c.className = cls; } return c; }
      tr.appendChild(td(r.Name || t('dashboard_lib_unknown')));
      tr.appendChild(td(hours(r.Minutes) + ' h', 'jellycrowd-admin-sub'));
      tr.appendChild(td((r.Plays || 0) + ' ×', 'jellycrowd-admin-sub'));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    section.appendChild(table);
    return section;
  }

  function render(d) {
    var content = document.getElementById('jcDashPane');
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
    if (d.RankByMinutes > 0 && d.RankedUsers > 1) {
      var rank = document.createElement('div');
      rank.className = 'jellycrowd-dash-rank';
      rank.textContent = t('dashboard_rank') + ' #' + d.RankByMinutes + ' / ' + d.RankedUsers;
      content.appendChild(rank);
    }
    if (d.Daily && d.Daily.length) { content.appendChild(chartSection(d.Daily)); }

    var grid = document.createElement('div');
    grid.className = 'jellycrowd-stat-grid';
    grid.appendChild(rankTable(t('stats_top_movies'), d.TopMovies));
    grid.appendChild(rankTable(t('stats_top_shows'), d.TopShows));
    content.appendChild(grid);
    if (d.ByLibrary && d.ByLibrary.length) { content.appendChild(libraryTable(d.ByLibrary)); }
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

  // ---------- viewing history (Dashboard sub-tab) ----------

  function historyRow(entry) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-history-row';

    var main = document.createElement('div');
    main.className = 'jellycrowd-history-main';
    var label = document.createElement('div');
    label.className = 'jellycrowd-history-label';
    label.textContent = lib.historyEntryLabel(entry);
    main.appendChild(label);

    var meta = document.createElement('div');
    meta.className = 'jellycrowd-history-meta';
    var when = entry.PlayedAtUtc ? new Date(entry.PlayedAtUtc) : null;
    var parts = [];
    if (when && !isNaN(when.getTime())) { parts.push(when.toLocaleDateString() + ' ' + when.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })); }
    if (entry.LibraryName) { parts.push(entry.LibraryName); }
    meta.textContent = parts.join(' · ');
    main.appendChild(meta);
    row.appendChild(main);

    var del = document.createElement('button');
    del.type = 'button';
    del.className = 'jellycrowd-history-del';
    del.textContent = '×';
    del.title = t('history_remove_one');
    del.setAttribute('aria-label', t('history_remove_one'));
    del.addEventListener('click', function () {
      del.disabled = true;
      apiPost('JellyCrowd/History/Mine/' + entry.Id + '/Delete')
        .then(function () { row.remove(); })
        .catch(function () { del.disabled = false; });
    });
    row.appendChild(del);
    return row;
  }

  function renderHistory(data) {
    var pane = document.getElementById('jcDashPane');
    if (!pane) { return; }
    pane.innerHTML = '';
    var hidden = !!(data && data.Hidden);
    var entries = (data && data.Entries) || [];

    var head = document.createElement('div');
    head.className = 'jellycrowd-history-head';
    head.appendChild(heading(t('history_title')));
    var actions = document.createElement('div');
    actions.className = 'jellycrowd-history-actions';
    var toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'jellycrowd-request';
    toggle.textContent = hidden ? t('history_show') : t('history_hide');
    toggle.addEventListener('click', function () {
      toggle.disabled = true;
      apiPost('JellyCrowd/History/Mine/Hidden?hidden=' + (hidden ? 'false' : 'true'))
        .then(loadHistory)
        .catch(function () { toggle.disabled = false; });
    });
    actions.appendChild(toggle);
    var clear = document.createElement('button');
    clear.type = 'button';
    clear.className = 'jellycrowd-request';
    clear.textContent = t('history_clear');
    clear.addEventListener('click', function () {
      confirmAction({ title: t('history_clear'), message: t('history_clear_confirm'), confirmLabel: t('history_clear') })
        .then(function (ok) {
          if (!ok) { return; }
          clear.disabled = true;
          apiPost('JellyCrowd/History/Mine/Clear').then(loadHistory).catch(function () { clear.disabled = false; });
        });
    });
    actions.appendChild(clear);
    head.appendChild(actions);
    pane.appendChild(head);

    if (hidden) {
      clear.hidden = true;
      var hmsg = document.createElement('p');
      hmsg.className = 'jellycrowd-field-hint';
      hmsg.textContent = t('history_hidden');
      pane.appendChild(hmsg);
      return;
    }

    if (!entries.length) {
      clear.hidden = true;
      var emsg = document.createElement('p');
      emsg.className = 'jellycrowd-field-hint';
      emsg.textContent = t('history_empty');
      pane.appendChild(emsg);
      return;
    }

    // Filters: keyword + inclusive date range.
    var filters = document.createElement('div');
    filters.className = 'jellycrowd-history-filters';
    var search = document.createElement('input');
    search.type = 'search';
    search.className = 'jellycrowd-search-input';
    search.placeholder = t('history_search');
    search.value = historyFilters.query;
    var from = document.createElement('input');
    from.type = 'date';
    from.value = historyFilters.from;
    from.setAttribute('aria-label', t('history_from'));
    var to = document.createElement('input');
    to.type = 'date';
    to.value = historyFilters.to;
    to.setAttribute('aria-label', t('history_to'));
    var fromWrap = document.createElement('label');
    fromWrap.className = 'jellycrowd-history-daterange';
    fromWrap.appendChild(document.createTextNode(t('history_from') + ' '));
    fromWrap.appendChild(from);
    var toWrap = document.createElement('label');
    toWrap.className = 'jellycrowd-history-daterange';
    toWrap.appendChild(document.createTextNode(t('history_to') + ' '));
    toWrap.appendChild(to);
    filters.appendChild(search);
    filters.appendChild(fromWrap);
    filters.appendChild(toWrap);
    pane.appendChild(filters);

    var listEl = document.createElement('div');
    listEl.className = 'jellycrowd-list';
    listEl.setAttribute('aria-live', 'polite');
    pane.appendChild(listEl);
    var noMatch = document.createElement('p');
    noMatch.className = 'jellycrowd-field-hint';
    noMatch.textContent = t('history_no_match');
    noMatch.hidden = true;
    pane.appendChild(noMatch);

    function apply() {
      historyFilters = { query: search.value, from: from.value, to: to.value };
      var shown = lib.filterHistory(entries, historyFilters);
      listEl.innerHTML = '';
      noMatch.hidden = shown.length > 0;
      shown.forEach(function (e) { listEl.appendChild(historyRow(e)); });
    }
    search.addEventListener('input', apply);
    from.addEventListener('change', apply);
    to.addEventListener('change', apply);
    apply();
  }

  function loadHistory() {
    setMessage(t('loading'));
    return apiGet('JellyCrowd/History/Mine')
      .then(function (d) { setMessage(''); renderHistory(d); })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  // Sub-tab shell: an Overview / History switcher above the active pane.
  function mount() {
    var content = document.getElementById('jcDashContent');
    if (!content) { return; }
    content.innerHTML = '';
    var tabs = document.createElement('div');
    tabs.className = 'jellycrowd-stats-period';
    [['overview', t('dashboard_overview')], ['history', t('history_title')]].forEach(function (tb) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jellycrowd-admin-tab' + (activeTab === tb[0] ? ' jellycrowd-admin-tab-active' : '');
      b.textContent = tb[1];
      b.addEventListener('click', function () { if (activeTab !== tb[0]) { activeTab = tb[0]; mount(); } });
      tabs.appendChild(b);
    });
    content.appendChild(tabs);
    var pane = document.createElement('div');
    pane.id = 'jcDashPane';
    content.appendChild(pane);
    if (activeTab === 'history') { loadHistory(); } else { load(); }
  }

  function init() {
    loadConfigLang().then(loadStrings).then(function () {
      var logo = document.getElementById('jcDashLogo');
      if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
      var title = document.getElementById('jcDashTitle');
      if (title) { title.textContent = t('dashboard_title'); }
      mount();
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('dashboard', function () { mount(); });
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
