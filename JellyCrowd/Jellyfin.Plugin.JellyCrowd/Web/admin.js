/*
 * Jelly Crowd — "Admin" overlay (admin only). A self-designed admin area with internal tabs, hosted by
 * the plugin and opened from the admin-only navbar entry. Built incrementally; this first cut hosts the
 * Moderation and Ownership tools (more tabs — Requests, Reports, Quotas, Logs — are being moved here from
 * the Dashboard config page). The backend endpoints are admin-protected; the navbar entry is admin-only.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w92';
  var PLUGIN_GUID = 'a1994160-4ea2-4d81-bd3c-ffe825700d98';
  var GIB = 1024 * 1024 * 1024;
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';
  var activeTab = null;
  var statsSessionTimer = null;   // live "now playing" poll (Stats tab); cleared when leaving the tab
  var usersById = {};   // userId -> display name (for the Requests/Reports tabs)

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

  function apiPostNoResult(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), contentType: 'application/json' });
    }
    return fetch(pluginUrl(path), { method: 'POST' }).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
    });
  }

  function apiPostJson(path, body) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), data: JSON.stringify(body), contentType: 'application/json' });
    }
    return fetch(pluginUrl(path), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      .then(function (r) { if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; } });
  }

  function apiPostResult(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path), dataType: 'json', contentType: 'application/json' });
    }
    return fetch(pluginUrl(path), { method: 'POST' }).then(function (r) {
      if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
      return r.json();
    });
  }

  function loadUsers() {
    if (!(window.ApiClient && typeof window.ApiClient.getUsers === 'function')) { return Promise.resolve(); }
    return window.ApiClient.getUsers()
      .then(function (users) { (users || []).forEach(function (u) { usersById[u.Id] = u.Name; }); })
      .catch(function () { /* best-effort */ });
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
    var el = document.getElementById('jcAdminMessage');
    if (!el) { return; }
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  // The admin tabs. `render(container)` fills the content area for that tab.
  var TABS = [
    { id: 'requests', labelKey: 'tab_requests', render: renderRequests },
    { id: 'stats', labelKey: 'tab_stats', render: renderStats },
    { id: 'reports', labelKey: 'admin_reports_title', render: renderReports },
    { id: 'quotas', labelKey: 'tab_quotas', render: renderQuotas },
    { id: 'moderation', labelKey: 'nav_moderation', render: renderModeration },
    { id: 'ownership', labelKey: 'nav_ownership', render: renderOwnership },
    { id: 'branding', labelKey: 'tab_branding', render: renderBranding },
    { id: 'logs', labelKey: 'tab_logs', render: renderLogs }
  ];

  // ---------- User quotas (per-user quota + policy, saved into the plugin configuration) ----------
  function renderQuotas(container) {
    container.innerHTML = '';
    setMessage(t('loading'));
    if (!(window.ApiClient && window.ApiClient.getPluginConfiguration && window.ApiClient.getUsers)) {
      setMessage(t('error_generic'));
      return;
    }
    Promise.all([
      window.ApiClient.getUsers(),
      apiGet('JellyCrowd/Quota/All').catch(function () { return []; }),
      window.ApiClient.getPluginConfiguration(PLUGIN_GUID)
    ]).then(function (res) {
      var users = res[0] || [];
      var usage = {};
      (res[1] || []).forEach(function (u) { usage[u.UserId] = u; });
      var overrides = (res[2] && res[2].QuotaOverrides) || [];
      setMessage('');

      var sub = document.createElement('p');
      sub.className = 'jellycrowd-disclaimer';
      sub.textContent = t('admin_quota_hint');
      container.appendChild(sub);

      function usageText(uid) {
        var u = usage[uid];
        if (!u) { return '—'; }
        var used = (u.UsedBytes / GIB).toFixed(1);
        if (u.Unlimited || u.QuotaBytes <= 0) { return used + ' / ∞ GiB'; }
        return used + ' / ' + (u.QuotaBytes / GIB).toFixed(1) + ' GiB' + (u.Tier && u.Tier !== 'base' ? ' (' + u.Tier + ')' : '');
      }

      var table = document.createElement('table');
      table.className = 'jellycrowd-admin-table';
      table.innerHTML = '<thead><tr><th>User</th><th>Usage</th><th>Quota (GiB)</th><th>Can request</th><th>Auto-approve</th><th>Req/period</th><th>Plugin access</th></tr></thead>';
      var tbody = document.createElement('tbody');
      users.forEach(function (user) {
        var ex = overrides.filter(function (o) { return o.UserId === user.Id; })[0] || {};
        var tr = document.createElement('tr');
        tr.setAttribute('data-userid', user.Id);
        function td(node) { var c = document.createElement('td'); c.appendChild(node); return c; }
        function textTd(text) { var c = document.createElement('td'); c.textContent = text; return c; }

        tr.appendChild(textTd(user.Name));
        var usageCell = textTd(usageText(user.Id));
        usageCell.style.whiteSpace = 'nowrap';
        tr.appendChild(usageCell);

        var quota = numberInput('jc-quota', ex.QuotaBytes != null ? (ex.QuotaBytes / GIB) : '');
        tr.appendChild(td(quota));
        var can = checkbox('jc-can', ex.CanRequest !== false);
        tr.appendChild(td(can));
        var auto = checkbox('jc-auto', ex.AutoApprove === true);
        tr.appendChild(td(auto));
        var cap = numberInput('jc-cap', ex.MaxRequestsPerPeriod != null ? ex.MaxRequestsPerPeriod : '');
        tr.appendChild(td(cap));
        var access = accessSelect(ex.PluginAccess);
        tr.appendChild(td(access));

        tbody.appendChild(tr);
      });
      table.appendChild(tbody);
      container.appendChild(table);

      var save = adminBtn(t('save'), 'ok', function (btn) {
        btn.disabled = true;
        // Re-read the live config so we don't clobber other settings, then write only QuotaOverrides.
        window.ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (cfg) {
          cfg.QuotaOverrides = collectQuotas(container);
          return window.ApiClient.updatePluginConfiguration(PLUGIN_GUID, cfg);
        }).then(function () {
          btn.disabled = false;
          btn.textContent = t('saved');
          setTimeout(function () { btn.textContent = t('save'); }, 1500);
        }).catch(function () { btn.disabled = false; });
      });
      save.style.marginTop = '1em';
      container.appendChild(save);
    }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function numberInput(cls, value) {
    var i = document.createElement('input');
    i.type = 'number';
    i.min = '0';
    i.step = '1';
    i.placeholder = 'default';
    i.className = cls;
    if (value !== '' && value != null) { i.value = value; }
    return i;
  }

  function checkbox(cls, checked) {
    var c = document.createElement('input');
    c.type = 'checkbox';
    c.className = cls;
    c.checked = !!checked;
    return c;
  }

  // Per-user plugin-access override (3-state, overrides "config mode"): Default (follow config mode) /
  // Enabled (always) / Disabled (never). Maps to PluginAccess null / true / false.
  function accessSelect(value) {
    var s = document.createElement('select');
    s.className = 'jc-access';
    [['', 'Default'], ['on', 'Enabled'], ['off', 'Disabled']].forEach(function (opt) {
      var o = document.createElement('option');
      o.value = opt[0];
      o.textContent = opt[1];
      s.appendChild(o);
    });
    s.value = value === true ? 'on' : (value === false ? 'off' : '');
    return s;
  }

  // Build the QuotaOverrides array — only entries that deviate from the defaults are kept (mirrors the
  // old config-page behaviour so the stored config stays minimal).
  function collectQuotas(container) {
    var result = [];
    container.querySelectorAll('tr[data-userid]').forEach(function (tr) {
      var uid = tr.getAttribute('data-userid');
      var quotaEl = tr.querySelector('.jc-quota');
      var capEl = tr.querySelector('.jc-cap');
      var canRequest = tr.querySelector('.jc-can').checked;
      var autoApprove = tr.querySelector('.jc-auto').checked;
      var access = tr.querySelector('.jc-access').value; // '' default, 'on' enabled, 'off' disabled
      var quotaSet = quotaEl && quotaEl.value !== '' && quotaEl.value !== null;
      var capSet = capEl && capEl.value !== '' && capEl.value !== null;
      if (!quotaSet && !capSet && canRequest && !autoApprove && access === '') {
        return; // all defaults → no entry
      }
      var o = { UserId: uid };
      if (quotaSet) { o.QuotaBytes = Math.round(parseFloat(quotaEl.value) * GIB); }
      if (!canRequest) { o.CanRequest = false; }
      if (autoApprove) { o.AutoApprove = true; }
      if (capSet) { o.MaxRequestsPerPeriod = parseInt(capEl.value, 10); }
      if (access === 'on') { o.PluginAccess = true; } else if (access === 'off') { o.PluginAccess = false; }
      result.push(o);
    });
    return result;
  }

  // ---------- Logs ----------
  function renderLogs(container) {
    container.innerHTML = '';
    setMessage('');
    var bar = document.createElement('div');
    bar.className = 'jellycrowd-admin-filter';
    var search = document.createElement('input');
    search.type = 'search';
    search.placeholder = t('log_search');
    var cat = document.createElement('select');
    [['', t('admin_filter_all')], ['request', 'request'], ['download', 'download'], ['admin', 'admin'], ['user', 'user'], ['system', 'system']]
      .forEach(function (o) { var x = document.createElement('option'); x.value = o[0]; x.textContent = o[1]; cat.appendChild(x); });
    var level = document.createElement('select');
    [['', t('admin_filter_all')], ['info', 'info'], ['warning', 'warning'], ['error', 'error']]
      .forEach(function (o) { var x = document.createElement('option'); x.value = o[0]; x.textContent = o[1]; level.appendChild(x); });
    bar.appendChild(search);
    bar.appendChild(cat);
    bar.appendChild(level);
    container.appendChild(bar);

    var box = document.createElement('div');
    box.className = 'jellycrowd-logs';
    container.appendChild(box);

    function load() {
      box.textContent = t('loading');
      var qs = 'term=' + encodeURIComponent(search.value || '') + '&category=' + encodeURIComponent(cat.value || '')
        + '&level=' + encodeURIComponent(level.value || '') + '&limit=200';
      apiGet('JellyCrowd/Logs?' + qs).then(function (rows) {
        box.innerHTML = '';
        if (!rows || !rows.length) { box.textContent = t('log_empty'); return; }
        var icons = { info: 'ℹ️', warning: '⚠️', error: '❌' };
        rows.forEach(function (r) {
          var row = document.createElement('div');
          row.className = 'jellycrowd-log-row';
          var when = document.createElement('span');
          when.className = 'jellycrowd-log-time';
          when.textContent = new Date(r.Timestamp).toLocaleString();
          var c = document.createElement('span');
          c.className = 'jellycrowd-log-cat';
          c.textContent = (icons[r.Level] || '•') + ' ' + r.Category;
          var m = document.createElement('span');
          m.textContent = r.Message;
          row.appendChild(when);
          row.appendChild(c);
          row.appendChild(m);
          box.appendChild(row);
        });
      }).catch(function () { box.textContent = t('error_generic'); });
    }

    var deb;
    search.addEventListener('input', function () { clearTimeout(deb); deb = setTimeout(load, 250); });
    cat.addEventListener('change', load);
    level.addEventListener('change', load);
    load();
  }

  // ---------- Requests (admin approval queue) ----------
  function statusToInt(s) {
    if (typeof s === 'number') { return s; }
    var map = { Pending: 0, Approved: 1, Denied: 2, Available: 3 };
    return map[s] != null ? map[s] : 0;
  }

  function isPending(r) { return r.Status === 0 || r.Status === 'Pending'; }

  function renderRequests(container) {
    container.innerHTML = '';
    // Status filter.
    var bar = document.createElement('div');
    bar.className = 'jellycrowd-admin-filter';
    var label = document.createElement('span');
    label.textContent = t('admin_filter_status');
    var filter = document.createElement('select');
    [['all', t('admin_filter_all')], ['0', t('status_pending')], ['1', t('status_approved')], ['3', t('status_available')], ['2', t('status_denied')]]
      .forEach(function (o) { var opt = document.createElement('option'); opt.value = o[0]; opt.textContent = o[1]; filter.appendChild(opt); });
    bar.appendChild(label);
    bar.appendChild(filter);
    container.appendChild(bar);

    var listHost = document.createElement('div');
    container.appendChild(listHost);

    function reload() { load(); }
    function decide(id, action) { apiPostNoResult('JellyCrowd/Requests/' + id + '/' + action).then(reload).catch(function () {}); }
    function adminDelete(id) { apiPostNoResult('JellyCrowd/Requests/' + id + '/Delete').then(reload).catch(function () {}); }
    function adminEdit(request, partial) {
      var body = {
        Status: partial.Status != null ? partial.Status : statusToInt(request.Status),
        Season: request.Season != null ? request.Season : null,
        Episode: request.Episode != null ? request.Episode : null,
        DesiredAt: Object.prototype.hasOwnProperty.call(partial, 'DesiredAt') ? partial.DesiredAt : (request.DesiredAt || null)
      };
      apiPostJson('JellyCrowd/Requests/' + request.Id + '/Edit', body).then(reload).catch(function () {});
    }

    function paint(requests) {
      var f = filter.value;
      var all = requests.slice().filter(function (r) { return f === 'all' || statusToInt(r.Status) === parseInt(f, 10); });
      all.sort(function (a, b) { return lib.statusRank(a.Status) - lib.statusRank(b.Status); });
      listHost.innerHTML = '';
      if (!all.length) { setMessage(requests.length ? t('admin_no_requests_filtered') : t('admin_no_requests')); return; }
      setMessage('');
      var table = document.createElement('table');
      table.className = 'jellycrowd-admin-table';
      table.innerHTML = '<thead><tr><th></th><th>' + t('col_title') + '</th><th>' + t('admin_requested_by') + '</th><th>' + t('col_date')
        + '</th><th>' + t('col_status') + '</th><th></th></tr></thead>';
      var tbody = document.createElement('tbody');
      all.forEach(function (request) { tbody.appendChild(requestRow(request, decide, adminEdit, adminDelete)); });
      table.appendChild(tbody);
      listHost.appendChild(table);
      loadDownloadStatus();
    }

    function load() {
      setMessage(t('loading'));
      apiGet('JellyCrowd/Requests').then(function (rows) { paint(rows || []); }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
    }

    function loadDownloadStatus() {
      apiGet('JellyCrowd/Requests/All/DownloadStatus').then(function (statuses) {
        (statuses || []).forEach(function (s) {
          var row = listHost.querySelector('tr[data-req-id="' + s.RequestId + '"]');
          if (!row) { return; }
          var cell = row.querySelector('.jellycrowd-admin-statuscell');
          if (!cell) { return; }
          var old = cell.querySelector('.jellycrowd-dl');
          if (old) { old.remove(); }
          if (s.State === 'downloading' || s.State === 'importing' || s.State === 'queued') {
            var badge = document.createElement('span');
            badge.className = 'jellycrowd-status jellycrowd-dl';
            var key = lib.downloadStateKey(s.State);
            var text = key ? t(key) : s.State;
            if (s.State === 'downloading' || s.State === 'importing') { text += ' ' + Math.round(s.Percent || 0) + '%'; }
            badge.textContent = text;
            cell.appendChild(badge);
          }
        });
      }).catch(function () { /* ignore */ });
    }

    filter.addEventListener('change', function () { load(); });
    load();
  }

  function requestRow(request, decide, adminEdit, adminDelete) {
    var tr = document.createElement('tr');
    tr.setAttribute('data-req-id', request.Id);

    var tdP = document.createElement('td');
    if (request.PosterPath) {
      var img = document.createElement('img');
      img.className = 'jellycrowd-admin-poster';
      img.loading = 'lazy';
      img.alt = '';
      img.src = POSTER_BASE + request.PosterPath;
      tdP.appendChild(img);
    }
    tr.appendChild(tdP);

    var tdT = document.createElement('td');
    var year = request.ReleaseDate ? (' (' + String(request.ReleaseDate).slice(0, 4) + ')') : '';
    tdT.textContent = request.Title + year + (request.Season ? ' · S' + request.Season : '') + (request.Episode ? 'E' + request.Episode : '');
    tr.appendChild(tdT);

    var tdU = document.createElement('td');
    tdU.textContent = usersById[request.UserId] || '?';
    tr.appendChild(tdU);

    var tdD = document.createElement('td');
    tdD.className = 'jellycrowd-admin-sub';
    tdD.textContent = request.RequestedAt ? new Date(request.RequestedAt).toLocaleDateString() : '';
    tr.appendChild(tdD);

    var tdS = document.createElement('td');
    tdS.className = 'jellycrowd-admin-statuscell';
    var statusKey = lib.requestStatusLabelKey(request);
    var status = document.createElement('span');
    status.className = 'jellycrowd-status jellycrowd-status-' + statusKey.replace('status_', '');
    status.textContent = t(statusKey);
    if (statusKey === 'status_held') {
      // A quota-held request isn't actually waiting on the admin — it resumes on its own.
      status.title = t('status_held_hint_admin');
    }
    tdS.appendChild(status);
    var isAvailable = request.Status === 3 || request.Status === 'Available';
    if (request.DispatchError && !isAvailable) {
      var err = document.createElement('span');
      err.className = 'jellycrowd-status jellycrowd-status-denied';
      err.textContent = '⚠ ' + t('dispatch_failed');
      err.title = request.DispatchError;
      tdS.appendChild(err);
    }
    tr.appendChild(tdS);

    var tdA = document.createElement('td');
    tdA.className = 'jellycrowd-admin-actions';
    if (isPending(request)) {
      tdA.appendChild(adminBtn(t('admin_approve'), 'ok', function () { decide(request.Id, 'Approve'); }));
      tdA.appendChild(adminBtn(t('admin_deny'), 'danger', function () { decide(request.Id, 'Deny'); }));
    }
    if (request.Status === 1 || request.Status === 'Approved') {
      tdA.appendChild(adminBtn(t('retry_search'), '', function () { decide(request.Id, 'Retry'); }));
    }
    var sel = document.createElement('select');
    [['0', 'status_pending'], ['1', 'status_approved'], ['2', 'status_denied'], ['3', 'status_available']]
      .forEach(function (p) { var o = document.createElement('option'); o.value = p[0]; o.textContent = t(p[1]); sel.appendChild(o); });
    sel.value = String(statusToInt(request.Status));
    sel.addEventListener('change', function () { adminEdit(request, { Status: parseInt(sel.value, 10) }); });
    tdA.appendChild(sel);
    var date = document.createElement('input');
    date.type = 'date';
    if (request.DesiredAt) { date.value = String(request.DesiredAt).slice(0, 10); }
    date.addEventListener('change', function () { adminEdit(request, { DesiredAt: date.value || null }); });
    tdA.appendChild(date);
    tdA.appendChild(adminBtn(t('admin_delete'), 'danger', function () { adminDelete(request.Id); }));
    tr.appendChild(tdA);
    return tr;
  }

  function adminBtn(label, kind, handler) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'jellycrowd-request' + (kind === 'danger' ? ' jellycrowd-request-danger' : (kind === 'ok' ? ' jellycrowd-request-ok' : ''));
    b.textContent = label;
    b.addEventListener('click', function () { handler(b); });
    return b;
  }

  // ---------- Statistics (JellyStats-style; admin only) ----------
  function statsHours(min) { return Math.round((min || 0) / 60); }

  function statsCard(value, label) {
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

  function statsHeading(text) {
    var h = document.createElement('h3');
    h.className = 'jellycrowd-branding-heading';
    h.textContent = text;
    return h;
  }

  function statsEmpty(text) {
    var p = document.createElement('p');
    p.className = 'jellycrowd-field-hint';
    p.textContent = text;
    return p;
  }

  // A ranked table (rank · name · plays · watch time). onRowClick makes rows clickable (drill-down).
  function statsRankTable(title, rows, nameOf, onRowClick) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-stat-section';
    section.appendChild(statsHeading(title));
    if (!rows || !rows.length) { section.appendChild(statsEmpty(t('stats_empty'))); return section; }
    var table = document.createElement('table');
    table.className = 'jellycrowd-admin-table';
    var tbody = document.createElement('tbody');
    rows.forEach(function (r, i) {
      var tr = document.createElement('tr');
      function td(text, cls) { var c = document.createElement('td'); c.textContent = text; if (cls) { c.className = cls; } return c; }
      tr.appendChild(td('#' + (i + 1), 'jellycrowd-admin-sub'));
      tr.appendChild(td(nameOf(r) || '—'));
      tr.appendChild(td(r.Plays + ' ' + t('stats_plays_unit'), 'jellycrowd-admin-sub'));
      tr.appendChild(td(statsHours(r.Minutes) + ' h', 'jellycrowd-admin-sub'));
      if (onRowClick) { tr.className = 'jellycrowd-row-click'; tr.addEventListener('click', function () { onRowClick(r); }); }
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    section.appendChild(table);
    return section;
  }

  function statsRecentTable(rows) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-stat-section';
    section.appendChild(statsHeading(t('stats_recent')));
    if (!rows || !rows.length) { section.appendChild(statsEmpty(t('stats_empty'))); return section; }
    var table = document.createElement('table');
    table.className = 'jellycrowd-admin-table';
    var tbody = document.createElement('tbody');
    rows.forEach(function (r) {
      var tr = document.createElement('tr');
      function td(text, cls) { var c = document.createElement('td'); c.textContent = text; if (cls) { c.className = cls; } return c; }
      tr.appendChild(td(r.UserName || '—'));
      tr.appendChild(td(r.Label || '—'));
      tr.appendChild(td(r.PlayedAtUtc ? new Date(r.PlayedAtUtc).toLocaleString() : '', 'jellycrowd-admin-sub'));
      tbody.appendChild(tr);
    });
    table.appendChild(tbody);
    section.appendChild(table);
    return section;
  }

  // A lightweight inline SVG bar chart of plays per day (stretched to the container width).
  function statsChart(daily) {
    var section = document.createElement('div');
    section.className = 'jellycrowd-stat-section';
    section.appendChild(statsHeading(t('stats_activity')));
    if (!daily || !daily.length) { section.appendChild(statsEmpty(t('stats_empty'))); return section; }
    var NS = 'http://www.w3.org/2000/svg';
    var max = 1;
    daily.forEach(function (d) { if (d.Plays > max) { max = d.Plays; } });
    var n = daily.length;
    var bw = 100 / n;
    var svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '0 0 100 32');
    svg.setAttribute('preserveAspectRatio', 'none');
    svg.setAttribute('class', 'jellycrowd-chart');
    daily.forEach(function (d, i) {
      var h = (d.Plays / max) * 30;
      var rect = document.createElementNS(NS, 'rect');
      rect.setAttribute('x', String(i * bw + bw * 0.12));
      rect.setAttribute('y', String(30 - (d.Plays > 0 ? Math.max(h, 0.6) : 0)));
      rect.setAttribute('width', String(bw * 0.76));
      rect.setAttribute('height', String(d.Plays > 0 ? Math.max(h, 0.6) : 0));
      rect.setAttribute('class', 'jellycrowd-chart-bar');
      var title = document.createElementNS(NS, 'title');
      title.textContent = d.Date + ': ' + d.Plays + ' ' + t('stats_plays_unit') + ' · ' + statsHours(d.Minutes) + ' h';
      rect.appendChild(title);
      svg.appendChild(rect);
    });
    section.appendChild(svg);
    return section;
  }

  function renderStats(container) {
    container.innerHTML = '';
    var windowDays = 30;
    var liveHost = document.createElement('div');
    var mainHost = document.createElement('div');
    container.appendChild(liveHost);
    container.appendChild(mainHost);

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

    function pollSessions() {
      apiGet('JellyCrowd/Stats/Sessions').then(function (sessions) {
        liveHost.innerHTML = '';
        liveHost.appendChild(statsHeading(t('stats_now_playing')));
        if (!sessions || !sessions.length) { liveHost.appendChild(statsEmpty(t('stats_nobody'))); return; }
        var table = document.createElement('table');
        table.className = 'jellycrowd-admin-table';
        var tbody = document.createElement('tbody');
        sessions.forEach(function (s) {
          var tr = document.createElement('tr');
          function td(text, cls) { var c = document.createElement('td'); c.textContent = text; if (cls) { c.className = cls; } return c; }
          tr.appendChild(td(s.UserName || '—'));
          tr.appendChild(td(s.Label || '—'));
          tr.appendChild(td(s.PositionPercent + '%' + (s.Paused ? ' · ' + t('stats_paused') : ''), 'jellycrowd-admin-sub'));
          tr.appendChild(td(s.Client || '', 'jellycrowd-admin-sub'));
          tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        liveHost.appendChild(table);
      }).catch(function () { /* best-effort live panel */ });
    }

    function showUser(u) {
      mainHost.innerHTML = '';
      setMessage(t('loading'));
      apiGet('JellyCrowd/Stats/User/' + encodeURIComponent(u.UserId) + '?windowDays=' + windowDays).then(function (d) {
        setMessage('');
        mainHost.innerHTML = '';
        d = d || {};
        var back = adminBtn(t('stats_back'), '', function () { load(); });
        mainHost.appendChild(back);
        mainHost.appendChild(statsHeading(u.Name || '—'));
        var cards = document.createElement('div');
        cards.className = 'jellycrowd-stat-cards';
        cards.appendChild(statsCard(statsHours(d.TotalMinutes) + ' h', t('stats_watchtime')));
        cards.appendChild(statsCard(d.TotalPlays || 0, t('stats_plays')));
        cards.appendChild(statsCard(d.RequestsTotal || 0, t('dashboard_req_total')));
        cards.appendChild(statsCard(d.RequestsAvailable || 0, t('dashboard_req_available')));
        mainHost.appendChild(cards);
        var grid = document.createElement('div');
        grid.className = 'jellycrowd-stat-grid';
        grid.appendChild(statsRankTable(t('stats_top_movies'), d.TopMovies, function (r) { return r.Name; }));
        grid.appendChild(statsRankTable(t('stats_top_shows'), d.TopShows, function (r) { return r.Name; }));
        mainHost.appendChild(grid);
        mainHost.appendChild(statsRecentTable(d.Recent));
      }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
    }

    // A dropdown of every Jellyfin user, so the admin can open ANY user's stats (not only the most
    // active ones listed above). Selecting a user reuses the same drill-down as the top-users table.
    function userPicker() {
      var wrap = document.createElement('div');
      wrap.className = 'jellycrowd-stats-userpicker';
      var label = document.createElement('span');
      label.className = 'jellycrowd-field-label';
      label.textContent = t('stats_view_user');
      var sel = document.createElement('select');
      sel.className = 'jellycrowd-select';
      var def = document.createElement('option');
      def.value = '';
      def.textContent = '…';
      sel.appendChild(def);
      if (window.ApiClient && window.ApiClient.getUsers) {
        window.ApiClient.getUsers().then(function (users) {
          (users || []).slice().sort(function (a, b) { return (a.Name || '').localeCompare(b.Name || ''); }).forEach(function (u) {
            var opt = document.createElement('option');
            opt.value = u.Id;
            opt.textContent = u.Name;
            sel.appendChild(opt);
          });
        }).catch(function () { /* best-effort */ });
      }
      sel.addEventListener('change', function () {
        if (sel.value) { showUser({ UserId: sel.value, Name: sel.options[sel.selectedIndex].textContent }); }
      });
      wrap.appendChild(label);
      wrap.appendChild(sel);
      return wrap;
    }

    // One-time backfill: pull viewing history from the Playback Reporting plugin's database. Safe to
    // re-run (only plays older than Jelly Crowd's own history are added).
    function importButton() {
      var wrap = document.createElement('div');
      wrap.className = 'jellycrowd-stats-import';
      var btn = adminBtn(t('stats_import_pr'), '', function () {
        if (!window.confirm(t('stats_import_confirm'))) { return; }
        btn.disabled = true;
        setMessage(t('stats_importing'));
        apiPostResult('JellyCrowd/Stats/ImportPlaybackReporting').then(function (r) {
          btn.disabled = false;
          r = r || {};
          if (!r.Found) { setMessage(t('stats_import_none')); return; }
          setMessage(t('stats_import_done').replace('{n}', r.Imported || 0));
          load();
        }).catch(function (e) { btn.disabled = false; setMessage(t(lib.errorKey(e && e.status))); });
      });
      var hint = document.createElement('div');
      hint.className = 'jellycrowd-field-hint';
      hint.textContent = t('stats_import_hint');
      wrap.appendChild(btn);
      wrap.appendChild(hint);
      return wrap;
    }

    function load() {
      setMessage(t('loading'));
      apiGet('JellyCrowd/Stats/Overview?windowDays=' + windowDays).then(function (o) {
        setMessage('');
        mainHost.innerHTML = '';
        o = o || {};
        mainHost.appendChild(periodBar(load));
        if (o.DataSinceUtc) {
          var since = document.createElement('div');
          since.className = 'jellycrowd-dash-since';
          since.textContent = t('dashboard_data_since') + ' ' + new Date(o.DataSinceUtc).toLocaleDateString();
          mainHost.appendChild(since);
        }
        mainHost.appendChild(statsChart(o.Daily));

        var cards = document.createElement('div');
        cards.className = 'jellycrowd-stat-cards';
        cards.appendChild(statsCard(o.TotalPlays || 0, t('stats_plays')));
        cards.appendChild(statsCard(statsHours(o.TotalMinutes) + ' h', t('stats_watchtime')));
        cards.appendChild(statsCard(o.UniqueUsers || 0, t('stats_viewers')));
        cards.appendChild(statsCard((o.LibraryMovies || 0) + ' · ' + (o.LibraryShows || 0) + ' · ' + (o.LibraryEpisodes || 0), t('stats_library')));
        mainHost.appendChild(cards);

        var grid = document.createElement('div');
        grid.className = 'jellycrowd-stat-grid';
        grid.appendChild(statsRankTable(t('stats_top_movies'), o.TopMovies, function (r) { return r.Name; }));
        grid.appendChild(statsRankTable(t('stats_top_shows'), o.TopShows, function (r) { return r.Name; }));
        grid.appendChild(statsRankTable(t('stats_top_users'), o.TopUsers, function (r) { return r.Name; }, showUser));
        mainHost.appendChild(grid);

        mainHost.appendChild(userPicker());
        mainHost.appendChild(statsRecentTable(o.Recent));
        mainHost.appendChild(importButton());
      }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
    }

    load();
    pollSessions();
    if (statsSessionTimer) { clearInterval(statsSessionTimer); }
    statsSessionTimer = setInterval(pollSessions, 10000);
  }

  // ---------- Branding (whole-UI theming, saved into the plugin configuration) ----------
  function textInput(cls, value, placeholder) {
    var i = document.createElement('input');
    i.type = 'text';
    i.className = cls + ' jellycrowd-text-input';
    if (placeholder) { i.placeholder = placeholder; }
    if (value != null) { i.value = value; }
    return i;
  }

  // A labelled form row: label on the left, control on the right, optional hint underneath.
  function field(labelText, control, hint) {
    var wrap = document.createElement('div');
    wrap.className = 'jellycrowd-field';
    var top = document.createElement('div');
    top.className = 'jellycrowd-field-top';
    var span = document.createElement('span');
    span.className = 'jellycrowd-field-label';
    span.textContent = labelText;
    top.appendChild(span);
    top.appendChild(control);
    wrap.appendChild(top);
    if (hint) {
      var h = document.createElement('div');
      h.className = 'jellycrowd-field-hint';
      h.textContent = hint;
      wrap.appendChild(h);
    }
    return wrap;
  }

  function sectionHeading(text) {
    var h = document.createElement('h3');
    h.className = 'jellycrowd-branding-heading';
    h.textContent = text;
    return h;
  }

  // A native colour picker synced with a text field, so named colours / rgba() values survive a save.
  function colorField(cls, value) {
    var wrap = document.createElement('span');
    wrap.className = 'jellycrowd-colorfield';
    var swatch = document.createElement('input');
    swatch.type = 'color';
    var text = document.createElement('input');
    text.type = 'text';
    text.className = cls + ' jellycrowd-text-input';
    text.placeholder = '#rrggbb';
    if (value) { text.value = value; }
    if (/^#[0-9a-fA-F]{6}$/.test(value || '')) { swatch.value = value; }
    swatch.addEventListener('input', function () { text.value = swatch.value; });
    text.addEventListener('input', function () { if (/^#[0-9a-fA-F]{6}$/.test(text.value)) { swatch.value = text.value; } });
    wrap.appendChild(swatch);
    wrap.appendChild(text);
    return wrap;
  }

  // POST a chosen file to the branding upload endpoint; resolves to the stored relative URL.
  function uploadBrandingImage(file) {
    var fd = new FormData();
    fd.append('file', file, file.name);
    var headers = {};
    if (window.ApiClient && window.ApiClient.accessToken) { headers['X-Emby-Token'] = window.ApiClient.accessToken(); }
    return fetch(pluginUrl('JellyCrowd/Branding/Upload'), { method: 'POST', body: fd, headers: headers })
      .then(function (r) { if (!r.ok) { var e = new Error('HTTP ' + r.status); e.status = r.status; throw e; } return r.json(); })
      .then(function (res) { return (res && res.Url) || ''; });
  }

  // An image field: a URL text input plus an "Upload" button that stores a file and fills the input.
  function imageInput(cls, value) {
    var input = textInput(cls, value, 'https://…');
    var wrap = document.createElement('span');
    wrap.className = 'jellycrowd-imagefield';
    var file = document.createElement('input');
    file.type = 'file';
    file.accept = 'image/*';
    file.style.display = 'none';
    var btn = adminBtn(t('branding_upload'), '', function () { file.click(); });
    btn.classList.add('jellycrowd-upload-btn');
    file.addEventListener('change', function () {
      if (!file.files || !file.files[0]) { return; }
      var orig = btn.textContent;
      btn.disabled = true; btn.textContent = '…';
      uploadBrandingImage(file.files[0]).then(function (rel) {
        if (rel) { input.value = rel; }
        btn.disabled = false; btn.textContent = orig;
      }).catch(function () { btn.disabled = false; btn.textContent = orig; setMessage(t('error_generic')); });
      file.value = '';
    });
    wrap.appendChild(input);
    wrap.appendChild(btn);
    wrap.appendChild(file);
    return { input: input, wrap: wrap };
  }

  function drawerLinkRow(l) {
    l = l || {};
    var row = document.createElement('div');
    row.className = 'jellycrowd-drawer-row';
    var name = textInput('jc-dl-name', l.Name || '', t('branding_drawer_name'));
    var url = textInput('jc-dl-url', l.Url || '', t('branding_drawer_url'));
    var icon = textInput('jc-dl-icon', l.Icon || '', t('branding_drawer_icon'));
    var newTabLabel = document.createElement('label');
    newTabLabel.className = 'jellycrowd-drawer-newtab';
    var newTab = checkbox('jc-dl-newtab', l.NewTab === true);
    newTabLabel.appendChild(newTab);
    newTabLabel.appendChild(document.createTextNode(' ' + t('branding_drawer_newtab')));
    var del = adminBtn('✕', 'danger', function () { if (row.parentNode) { row.parentNode.removeChild(row); } });
    del.classList.add('jellycrowd-drawer-del');
    row.appendChild(name);
    row.appendChild(url);
    row.appendChild(icon);
    row.appendChild(newTabLabel);
    row.appendChild(del);
    return row;
  }

  function collectDrawerLinks(wrap) {
    var out = [];
    wrap.querySelectorAll('.jellycrowd-drawer-row').forEach(function (row) {
      var name = row.querySelector('.jc-dl-name').value.trim();
      var url = row.querySelector('.jc-dl-url').value.trim();
      if (!name || !url) { return; } // an entry needs at least a label and a destination
      out.push({
        Name: name,
        Url: url,
        Icon: row.querySelector('.jc-dl-icon').value.trim(),
        NewTab: row.querySelector('.jc-dl-newtab').checked
      });
    });
    return out;
  }

  function renderBranding(container) {
    container.innerHTML = '';
    setMessage(t('loading'));
    if (!(window.ApiClient && window.ApiClient.getPluginConfiguration && window.ApiClient.updatePluginConfiguration)) {
      setMessage(t('error_generic'));
      return;
    }
    window.ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (cfg) {
      setMessage('');
      var b = cfg || {};

      var intro = document.createElement('p');
      intro.className = 'jellycrowd-disclaimer';
      intro.textContent = t('branding_intro');
      container.appendChild(intro);

      var enabled = checkbox('jc-b-enabled', b.BrandingEnabled === true);
      container.appendChild(field(t('branding_enabled'), enabled, t('branding_enabled_hint')));

      container.appendChild(sectionHeading(t('branding_identity')));
      var logoF = imageInput('jc-b-logo', b.BrandingLogoUrl || '');
      var logo = logoF.input;
      container.appendChild(field(t('branding_logo'), logoF.wrap));
      var faviconF = imageInput('jc-b-favicon', b.BrandingFaviconUrl || '');
      var favicon = faviconF.input;
      container.appendChild(field(t('branding_favicon'), faviconF.wrap));
      var avatarF = imageInput('jc-b-avatar', b.BrandingDefaultAvatarUrl || '');
      var avatar = avatarF.input;
      container.appendChild(field(t('branding_avatar'), avatarF.wrap, t('branding_avatar_hint')));

      container.appendChild(sectionHeading(t('branding_appearance')));
      var bgF = imageInput('jc-b-bgurl', b.BrandingBackgroundUrl || '');
      var bgUrl = bgF.input;
      container.appendChild(field(t('branding_background'), bgF.wrap));
      container.appendChild(field(t('branding_background_color'), colorField('jc-b-bgcolor', b.BrandingBackgroundColor || '')));
      container.appendChild(field(t('branding_accent'), colorField('jc-b-accent', b.BrandingAccentColor || '')));
      var fontFamily = textInput('jc-b-font', b.BrandingFontFamily || '', 'Inter, sans-serif');
      container.appendChild(field(t('branding_font'), fontFamily));
      var fontUrl = textInput('jc-b-fonturl', b.BrandingFontUrl || '', 'https://fonts.googleapis.com/…');
      container.appendChild(field(t('branding_font_url'), fontUrl, t('branding_font_url_hint')));

      container.appendChild(sectionHeading(t('branding_presets')));
      var pCompact = checkbox('jc-b-p-compact', b.BrandingPresetCompactEpisodes === true);
      container.appendChild(field(t('branding_preset_compact'), pCompact));
      var pDark = checkbox('jc-b-p-dark', b.BrandingPresetDarkIndicators === true);
      container.appendChild(field(t('branding_preset_dark'), pDark));
      var pNarrow = checkbox('jc-b-p-narrow', b.BrandingPresetNarrowChannels === true);
      container.appendChild(field(t('branding_preset_narrow'), pNarrow));
      var pBackdrop = checkbox('jc-b-p-backdrop', b.BrandingPresetHideBackdrop === true);
      container.appendChild(field(t('branding_preset_backdrop'), pBackdrop));
      var pButtons = checkbox('jc-b-p-buttons', b.BrandingPresetButtonTweaks === true);
      container.appendChild(field(t('branding_preset_buttons'), pButtons));

      container.appendChild(sectionHeading(t('branding_drawer')));
      var drawerHint = document.createElement('p');
      drawerHint.className = 'jellycrowd-field-hint';
      drawerHint.textContent = t('branding_drawer_hint');
      container.appendChild(drawerHint);
      var drawerWrap = document.createElement('div');
      drawerWrap.className = 'jellycrowd-drawer-list';
      (b.BrandingDrawerLinks || []).forEach(function (l) { drawerWrap.appendChild(drawerLinkRow(l)); });
      container.appendChild(drawerWrap);
      var addLink = adminBtn(t('branding_drawer_add'), '', function () { drawerWrap.appendChild(drawerLinkRow({})); });
      container.appendChild(addLink);

      container.appendChild(sectionHeading(t('branding_custom_css')));
      var cssArea = document.createElement('textarea');
      cssArea.className = 'jc-b-css jellycrowd-css-input';
      cssArea.rows = 12;
      cssArea.spellcheck = false;
      cssArea.placeholder = '.backgroundContainer { … }';
      cssArea.value = b.BrandingCustomCss || '';
      container.appendChild(cssArea);

      var save = adminBtn(t('save'), 'ok', function (btn) {
        btn.disabled = true;
        // Re-read the live config so other settings aren't clobbered, then write only the Branding fields.
        window.ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (live) {
          live.BrandingEnabled = enabled.checked;
          live.BrandingLogoUrl = logo.value.trim();
          live.BrandingFaviconUrl = favicon.value.trim();
          live.BrandingDefaultAvatarUrl = avatar.value.trim();
          live.BrandingBackgroundUrl = bgUrl.value.trim();
          live.BrandingBackgroundColor = container.querySelector('.jc-b-bgcolor').value.trim();
          live.BrandingAccentColor = container.querySelector('.jc-b-accent').value.trim();
          live.BrandingFontFamily = fontFamily.value.trim();
          live.BrandingFontUrl = fontUrl.value.trim();
          live.BrandingPresetCompactEpisodes = pCompact.checked;
          live.BrandingPresetDarkIndicators = pDark.checked;
          live.BrandingPresetNarrowChannels = pNarrow.checked;
          live.BrandingPresetHideBackdrop = pBackdrop.checked;
          live.BrandingPresetButtonTweaks = pButtons.checked;
          live.BrandingCustomCss = cssArea.value;
          live.BrandingDrawerLinks = collectDrawerLinks(drawerWrap);
          return window.ApiClient.updatePluginConfiguration(PLUGIN_GUID, live);
        }).then(function () {
          btn.disabled = false;
          btn.textContent = t('saved');
          setTimeout(function () { btn.textContent = t('save'); }, 1500);
        }).catch(function () { btn.disabled = false; setMessage(t('error_generic')); });
      });
      save.style.marginTop = '1.2em';
      container.appendChild(save);
    }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  // ---------- Reports ----------
  function renderReports(container) {
    setMessage(t('loading'));
    function reload() { renderReports(container); }
    apiGet('JellyCrowd/Reports')
      .then(function (reports) {
        container.innerHTML = '';
        reports = reports || [];
        if (!reports.length) { setMessage(t('admin_no_reports')); return; }
        setMessage('');
        var table = document.createElement('table');
        table.className = 'jellycrowd-admin-table';
        var tbody = document.createElement('tbody');
        reports.forEach(function (r) {
          var tr = document.createElement('tr');
          if (r.Resolved) { tr.style.opacity = '0.55'; }
          var tdMain = document.createElement('td');
          var title = document.createElement('div');
          title.style.fontWeight = '600';
          title.textContent = r.Title + (r.Resolved ? ' ✓' : '');
          var msg = document.createElement('div');
          msg.className = 'jellycrowd-admin-sub';
          msg.textContent = r.Message;
          var sub = document.createElement('div');
          sub.className = 'jellycrowd-admin-sub';
          sub.textContent = t('report_type_' + (r.Type || 'other')) + ' · ' + (usersById[r.UserId] || r.UserName || '?') + ' · ' + (r.CreatedAt ? new Date(r.CreatedAt).toLocaleString() : '');
          tdMain.appendChild(title);
          tdMain.appendChild(msg);
          tdMain.appendChild(sub);
          if (r.AdminResponse) {
            var resp = document.createElement('div');
            resp.className = 'jellycrowd-admin-sub';
            resp.textContent = '↳ ' + r.AdminResponse;
            tdMain.appendChild(resp);
          }
          tr.appendChild(tdMain);
          var tdA = document.createElement('td');
          tdA.className = 'jellycrowd-admin-actions';
          if (!r.Resolved) {
            var respInput = document.createElement('input');
            respInput.type = 'text';
            respInput.className = 'jellycrowd-report-response';
            respInput.placeholder = t('admin_resolve_response');
            tdA.appendChild(respInput);
            tdA.appendChild(adminBtn(t('admin_resolve'), '', function () {
              apiPostJson('JellyCrowd/Reports/' + r.Id + '/Resolve', { Response: respInput.value }).then(reload).catch(function () {});
            }));
          }
          tdA.appendChild(adminBtn(t('admin_delete'), 'danger', function () {
            apiPostNoResult('JellyCrowd/Reports/' + r.Id + '/Delete').then(reload).catch(function () {});
          }));
          tr.appendChild(tdA);
          tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        container.appendChild(table);
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  // ---------- Moderation ----------
  function renderModeration(container) {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Comments/All')
      .then(function (reviews) {
        container.innerHTML = '';
        reviews = reviews || [];
        if (!reviews.length) { setMessage(t('moderation_empty')); return; }
        setMessage('');
        var sub = document.createElement('p');
        sub.className = 'jellycrowd-disclaimer';
        sub.textContent = t('moderation_subtitle');
        container.appendChild(sub);
        reviews.forEach(function (r) { container.appendChild(moderationRow(r, container)); });
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function titleLabel(item) {
    var typeLabel = (item.MediaType === 'tv' ? t('media_type_tv') : t('media_type_movie'));
    return item.Title ? (item.Title + ' (' + typeLabel + ')') : (typeLabel + ' #' + item.TmdbId);
  }

  function tmdbLink(item) {
    var a = document.createElement('a');
    a.href = 'https://www.themoviedb.org/' + item.MediaType + '/' + item.TmdbId;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.className = 'jellycrowd-link';
    a.textContent = titleLabel(item);
    return a;
  }

  function actionButton(label, handler, secondary) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'jellycrowd-request' + (secondary ? ' jellycrowd-request-secondary' : '');
    b.textContent = label;
    b.addEventListener('click', function () { handler(b); });
    return b;
  }

  function moderationRow(review, container) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-mod-row';

    var head = document.createElement('div');
    head.className = 'jellycrowd-mod-head';
    head.appendChild(tmdbLink(review));
    if (review.Rating > 0) {
      var rating = document.createElement('span');
      rating.className = 'jellycrowd-mod-rating';
      rating.textContent = '★ ' + review.Rating + '/10';
      head.appendChild(rating);
    }
    var author = document.createElement('span');
    author.className = 'jellycrowd-mod-author';
    author.textContent = review.UserName || '—';
    head.appendChild(author);
    var when = document.createElement('span');
    when.className = 'jellycrowd-mod-date';
    when.textContent = review.CreatedAt ? new Date(review.CreatedAt).toLocaleString() : '';
    head.appendChild(when);
    if (review.Hidden) {
      var badge = document.createElement('span');
      badge.className = 'jellycrowd-mod-hidden-badge';
      badge.textContent = t('moderation_hidden');
      head.appendChild(badge);
    }
    row.appendChild(head);

    if (review.Text) {
      var text = document.createElement('div');
      text.className = 'jellycrowd-mod-text';
      text.textContent = review.Text;
      row.appendChild(text);
    }

    var actions = document.createElement('div');
    actions.className = 'jellycrowd-mod-actions';
    actions.appendChild(actionButton(review.Hidden ? t('comment_show') : t('comment_hide'), function (btn) {
      btn.disabled = true;
      apiPostNoResult('JellyCrowd/Comments/' + review.Id + (review.Hidden ? '/Show' : '/Hide'))
        .then(function () { renderModeration(container); })
        .catch(function () { btn.disabled = false; });
    }, true));
    actions.appendChild(actionButton(t('comment_delete'), function (btn) {
      btn.disabled = true;
      apiPostNoResult('JellyCrowd/Comments/' + review.Id + '/Delete')
        .then(function () { renderModeration(container); })
        .catch(function () { btn.disabled = false; });
    }));
    row.appendChild(actions);
    return row;
  }

  // ---------- Ownership ----------
  function renderOwnership(container) {
    setMessage(t('loading'));
    apiGet('JellyCrowd/Requests/Ownerships')
      .then(function (items) {
        container.innerHTML = '';
        items = items || [];
        var sub = document.createElement('p');
        sub.className = 'jellycrowd-disclaimer';
        sub.textContent = t('ownership_subtitle');
        container.appendChild(sub);

        var filter = document.createElement('input');
        filter.type = 'search';
        filter.className = 'jellycrowd-search-input';
        filter.placeholder = t('ownership_filter_placeholder');
        container.appendChild(filter);

        var list = document.createElement('div');
        list.className = 'jellycrowd-list';
        container.appendChild(list);

        function paint() {
          var q = (filter.value || '').trim().toLowerCase();
          var shown = items.filter(function (it) {
            if (!q) { return true; }
            if ((it.Title || '').toLowerCase().indexOf(q) >= 0) { return true; }
            return (it.Owners || []).some(function (o) { return (o.Name || '').toLowerCase().indexOf(q) >= 0; });
          });
          list.innerHTML = '';
          if (!shown.length) { setMessage(q ? t('no_results') : t('ownership_empty')); return; }
          setMessage('');
          shown.forEach(function (it) { list.appendChild(ownershipRow(it)); });
        }
        filter.addEventListener('input', paint);
        paint();
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function ownershipRow(item) {
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
    var title = tmdbLink(item);
    title.classList.add('jellycrowd-own-title');
    head.appendChild(title);
    if (item.Season != null) {
      var scope = document.createElement('span');
      scope.className = 'jellycrowd-own-scope';
      scope.textContent = 'S' + item.Season + (item.Episode != null ? 'E' + item.Episode : '');
      head.appendChild(scope);
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
      if (o.SinceUtc) { chip.title = t('ownership_since') + ' ' + new Date(o.SinceUtc).toLocaleDateString(); }
      owners.appendChild(chip);
    });
    body.appendChild(owners);
    row.appendChild(body);
    return row;
  }

  // ---------- shell ----------
  function activate(id) {
    activeTab = id;
    if (statsSessionTimer) { clearInterval(statsSessionTimer); statsSessionTimer = null; } // stop the live poll when leaving Stats
    var bar = document.getElementById('jcAdminTabs');
    [].forEach.call(bar.querySelectorAll('.jellycrowd-admin-tab'), function (b) {
      b.classList.toggle('jellycrowd-admin-tab-active', b.getAttribute('data-tab') === id);
    });
    var content = document.getElementById('jcAdminContent');
    content.innerHTML = '';
    setMessage('');
    var tab = null;
    TABS.forEach(function (x) { if (x.id === id) { tab = x; } });
    if (tab) { tab.render(content); }
  }

  function buildTabBar() {
    var bar = document.getElementById('jcAdminTabs');
    bar.innerHTML = '';
    TABS.forEach(function (tab) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jellycrowd-admin-tab';
      b.setAttribute('data-tab', tab.id);
      b.setAttribute('role', 'tab');
      b.textContent = t(tab.labelKey);
      b.addEventListener('click', function () { activate(tab.id); });
      bar.appendChild(b);
    });
  }

  function init() {
    loadConfigLang().then(loadStrings).then(loadUsers).then(function () {
      var logo = document.getElementById('jcAdminLogo');
      if (logo) { logo.src = pluginUrl('JellyCrowd/Web/logo.png'); }
      document.getElementById('jcAdminTitle').textContent = t('nav_admin');
      buildTabBar();
      activate(TABS[0].id);
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('admin', function () { if (activeTab) { activate(activeTab); } });
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
