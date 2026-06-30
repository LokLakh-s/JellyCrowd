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
        var access = checkbox('jc-access', ex.PluginAccess === true);
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
      var pluginAccess = tr.querySelector('.jc-access').checked;
      var quotaSet = quotaEl && quotaEl.value !== '' && quotaEl.value !== null;
      var capSet = capEl && capEl.value !== '' && capEl.value !== null;
      if (!quotaSet && !capSet && canRequest && !autoApprove && !pluginAccess) {
        return; // all defaults → no entry
      }
      var o = { UserId: uid };
      if (quotaSet) { o.QuotaBytes = Math.round(parseFloat(quotaEl.value) * GIB); }
      if (!canRequest) { o.CanRequest = false; }
      if (autoApprove) { o.AutoApprove = true; }
      if (capSet) { o.MaxRequestsPerPeriod = parseInt(capEl.value, 10); }
      if (pluginAccess) { o.PluginAccess = true; }
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
          sub.textContent = (usersById[r.UserId] || r.UserName || '?') + ' · ' + (r.CreatedAt ? new Date(r.CreatedAt).toLocaleString() : '');
          tdMain.appendChild(title);
          tdMain.appendChild(msg);
          tdMain.appendChild(sub);
          tr.appendChild(tdMain);
          var tdA = document.createElement('td');
          tdA.className = 'jellycrowd-admin-actions';
          if (!r.Resolved) {
            tdA.appendChild(adminBtn(t('admin_resolve'), '', function () {
              apiPostNoResult('JellyCrowd/Reports/' + r.Id + '/Resolve').then(reload).catch(function () {});
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
