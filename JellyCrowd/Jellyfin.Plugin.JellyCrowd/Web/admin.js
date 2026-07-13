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

  // The admin tabs, ordered by how often an admin uses them (most-used first). Some tabs group related
  // views under sub-tabs (see subTabs): Moderation = Reports + Reviews; Users = Per-user + Ownership.
  // `render(container)` fills the content area for that tab.
  var TABS = [
    { id: 'requests', labelKey: 'tab_requests', render: renderRequests },
    { id: 'stats', labelKey: 'tab_stats', render: renderStats },
    { id: 'moderation', labelKey: 'nav_moderation', render: renderModeration },
    { id: 'users', labelKey: 'tab_users', render: renderUsers },
    { id: 'logs', labelKey: 'tab_logs', render: renderLogs },
    { id: 'configurations', labelKey: 'tab_configurations', render: renderConfigurations }
  ];

  // Renders a secondary sub-tab bar (reusing the admin-tab styling) plus a content host inside `container`,
  // and shows the active sub-tab. `subs` = [{ id, labelKey, render(host) }].
  function subTabs(container, subs, initialId) {
    var bar = document.createElement('div');
    bar.className = 'jellycrowd-stats-period jellycrowd-admin-subtabs';
    var host = document.createElement('div');
    var active = initialId || subs[0].id;
    function activateSub(id) {
      active = id;
      [].forEach.call(bar.querySelectorAll('.jellycrowd-admin-tab'), function (b) {
        b.classList.toggle('jellycrowd-admin-tab-active', b.getAttribute('data-subtab') === id);
      });
      host.innerHTML = '';
      subs.forEach(function (s) { if (s.id === id) { s.render(host); } });
    }
    subs.forEach(function (s) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'jellycrowd-admin-tab';
      b.setAttribute('data-subtab', s.id);
      b.textContent = t(s.labelKey);
      b.addEventListener('click', function () { activateSub(s.id); });
      bar.appendChild(b);
    });
    container.appendChild(bar);
    container.appendChild(host);
    activateSub(active);
  }

  // Merged tab: user-submitted reports + ratings/reviews moderation.
  function renderModeration(container) {
    subTabs(container, [
      { id: 'reports', labelKey: 'admin_reports_title', render: renderReports },
      { id: 'reviews', labelKey: 'tab_reviews', render: renderReviews }
    ]);
  }

  // Merged tab: per-user overrides (quota + access) and media ownership.
  function renderUsers(container) {
    subTabs(container, [
      { id: 'peruser', labelKey: 'tab_per_user', render: renderQuotas },
      { id: 'ownership', labelKey: 'nav_ownership', render: renderOwnership }
    ]);
  }

  // ---------- Configurations (the plugin settings, moved here from the Dashboard config page) ----------
  function selectInput(cls, value, options) {
    var s = document.createElement('select');
    s.className = cls + ' jellycrowd-text-input';
    options.forEach(function (o) {
      var opt = document.createElement('option');
      opt.value = o[0]; opt.textContent = o[1];
      if (String(value) === String(o[0])) { opt.selected = true; }
      s.appendChild(opt);
    });
    return s;
  }

  // Builds config fields from a spec list into `host`; returns { apply(liveCfg) } to write the values back.
  // spec: { key, label, type:'text'|'num'|'check'|'select'|'section', hint, placeholder, options, scale }
  function cfgForm(host, cfg, specs) {
    var controls = {};
    specs.forEach(function (s) {
      if (s.type === 'section') { host.appendChild(sectionHeading(s.label)); return; }
      var cls = 'jc-c-' + s.key;
      var v = cfg[s.key];
      var ctrl, vc;
      if (s.type === 'check') { ctrl = vc = checkbox(cls, v === true); }
      else if (s.type === 'select') { ctrl = vc = selectInput(cls, v, s.options); }
      else if (s.type === 'color') { ctrl = colorField(cls, v || ''); vc = ctrl.querySelector('.' + cls); }
      else if (s.type === 'area') { ctrl = vc = document.createElement('textarea'); ctrl.className = cls + ' jellycrowd-text-input'; ctrl.rows = s.rows || 3; ctrl.spellcheck = false; if (v != null) { ctrl.value = v; } }
      else if (s.type === 'num') { ctrl = vc = numberInput(cls, s.scale ? (v ? (v / s.scale) : '') : (v != null ? v : '')); }
      else { ctrl = vc = textInput(cls, v != null ? v : '', s.placeholder); }
      controls[s.key] = { s: s, c: vc };
      host.appendChild(field(s.label, ctrl, s.hint));
    });
    return {
      apply: function (live) {
        Object.keys(controls).forEach(function (k) {
          var s = controls[k].s, c = controls[k].c;
          if (s.type === 'check') { live[k] = c.checked; }
          else if (s.type === 'num') { var n = parseFloat(c.value || '0') || 0; live[k] = s.scale ? Math.round(n * s.scale) : Math.round(n); }
          else { live[k] = c.value != null ? c.value.toString() : ''; }
        });
      }
    };
  }

  // A Save button that re-reads the live config (so untouched settings are never clobbered), applies each
  // form's values and persists.
  function cfgSaveButton(forms) {
    var save = adminBtn(t('save'), 'ok', function (btn) {
      btn.disabled = true;
      window.ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (live) {
        forms.forEach(function (f) { f.apply(live); });
        return window.ApiClient.updatePluginConfiguration(PLUGIN_GUID, live);
      }).then(function () {
        btn.disabled = false; btn.textContent = t('saved');
        setTimeout(function () { btn.textContent = t('save'); }, 1500);
      }).catch(function () { btn.disabled = false; setMessage(t('error_generic')); });
    });
    save.style.marginTop = '1.2em';
    return save;
  }

  // Loads the plugin config, then calls build(host, cfg) to render a settings sub-tab.
  function cfgLoad(container, build) {
    container.innerHTML = '';
    setMessage(t('loading'));
    if (!(window.ApiClient && window.ApiClient.getPluginConfiguration && window.ApiClient.updatePluginConfiguration)) { setMessage(t('error_generic')); return; }
    window.ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (cfg) {
      setMessage('');
      build(container, cfg || {});
    }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
  }

  function renderConfigurations(container) {
    subTabs(container, [
      { id: 'general', labelKey: 'cfg_general', render: renderCfgGeneral },
      { id: 'requests', labelKey: 'cfg_requests', render: renderCfgRequests },
      { id: 'notifications', labelKey: 'cfg_notifications', render: renderCfgNotifications },
      { id: 'download', labelKey: 'cfg_download', render: renderCfgDownload },
      { id: 'branding', labelKey: 'tab_branding', render: renderBranding },
      { id: 'diagnostics', labelKey: 'cfg_diagnostics', render: renderCfgDiagnostics }
    ]);
  }

  function renderCfgGeneral(container) {
    cfgLoad(container, function (host, cfg) {
      var form = cfgForm(host, cfg, [
        { key: 'Language', label: 'Language', type: 'select', options: [['auto', "Auto (follow each user's language)"], ['en', 'English'], ['fr', 'Français']], hint: 'Language of the Jelly Crowd user pages and notifications.' },
        { key: 'HiddenFromUsers', label: 'Config mode — hide the plugin from regular users', type: 'check', hint: 'Hide Jelly Crowd from non-admins until it is configured and working. Admins are unaffected.' },
        { key: 'RateLimitPerMinute', label: 'API rate limit (writes / minute / user)', type: 'num', hint: '0 disables it.' },
        { key: 'RateLimitGetPerMinute', label: 'API rate limit (reads / minute / user)', type: 'num', hint: 'Generous cap so a scripted loop cannot hammer the TMDB-backed catalog. 0 disables it.' },
        { key: 'CommentsEnabled', label: 'Enable ratings & reviews', type: 'check' },
        { key: 'ShowReviewAuthors', label: 'Show review author names to everyone', type: 'check' },
        { key: 'SkipOutroEnabled', label: 'Detect end credits (Skip Outro)', type: 'check' },
        { key: 'SkipIntroEnabled', label: 'Detect episode intros (Skip Intro)', type: 'check' },
        { key: 'SegmentHwAccel', label: 'Skip Outro — GPU acceleration', type: 'select', options: [['auto', 'Auto (GPU if available)'], ['none', 'CPU only'], ['vaapi', 'Intel / AMD (VAAPI)'], ['qsv', 'Intel QuickSync (QSV)'], ['cuda', 'NVIDIA (CUDA)'], ['videotoolbox', 'macOS (VideoToolbox)']], hint: 'Offloads the end-credits video decode to the GPU during the Media Segment Scan — the heavy part. Auto falls back to CPU when no GPU decoder is available. (The Skip Intro fingerprint is audio-only, so it is unaffected.)' },
        { key: 'TmdbApiKey', label: 'TMDB API key', type: 'text', placeholder: 'your TMDB API key' }
      ]);
      host.appendChild(cfgSaveButton([form]));
    });
  }

  function renderCfgRequests(container) {
    cfgLoad(container, function (host, cfg) {
      var reqForm = cfgForm(host, cfg, [
        { type: 'section', label: 'Requests' },
        { key: 'RequireApproval', label: 'Require admin approval for requests', type: 'check' },
        { key: 'AllowUserRetrySearch', label: 'Let users retry the search themselves', type: 'check' },
        { key: 'MaxRequestsPerPeriod', label: 'Max requests per period', type: 'num', hint: '0 = unlimited.' },
        { key: 'RequestPeriod', label: 'Request period', type: 'select', options: [['Day', 'Day'], ['Week', 'Week'], ['Month', 'Month']] },
        { key: 'AutoApproveMaxSizeBytes', label: 'Auto-approve if estimated size ≤ (GiB)', type: 'num', scale: GIB, hint: '0 disables size-based auto-approval.' },
        { key: 'EstimatedMovieSizeBytes', label: 'Estimated movie size (GiB)', type: 'num', scale: GIB },
        { key: 'EstimatedEpisodeSizeBytes', label: 'Estimated episode size (GiB)', type: 'num', scale: GIB }
      ]);
      var genresInput = textInput('jc-c-AutoApproveGenres', (cfg.AutoApproveGenres || []).join(', '), 'Action, Comedy, Documentary…');
      host.appendChild(field('Auto-approve genres (optional)', genresInput, 'Comma-separated TMDB genre names. When set, the size rule above only auto-approves a title with at least one of these genres. Empty = all genres. Trusted users always bypass.'));
      var genresForm = { apply: function (live) { live.AutoApproveGenres = genresInput.value.split(',').map(function (g) { return g.trim(); }).filter(Boolean); } };
      var quotaForm = cfgForm(host, cfg, [
        { type: 'section', label: 'Quotas' },
        { key: 'DefaultUserQuotaBytes', label: 'Default quota per user (GiB)', type: 'num', scale: GIB, hint: '0 = unlimited.' }
      ]);
      var adaptEnable = checkbox('jc-c-AdaptiveQuotaEnabled', cfg.AdaptiveQuotaEnabled === true);
      host.appendChild(field('Adaptive quota (reward active users)', adaptEnable, 'Elevate active users above, and decay inactive users below, their base quota.'));
      var adaptWrap = document.createElement('div');
      var adapt = cfgForm(adaptWrap, cfg, [
        { key: 'AdaptiveFloorPercent', label: 'Floor tier (% of base)', type: 'num' },
        { key: 'AdaptiveCeilingPercent', label: 'Ceiling tier (% of base)', type: 'num' },
        { key: 'AdaptiveWindowDays', label: 'Activity window (days)', type: 'num' },
        { key: 'AdaptiveMinMinutes', label: 'Active: minimum watch minutes', type: 'num' },
        { key: 'AdaptiveMinActiveDays', label: 'Active: minimum distinct days', type: 'num' },
        { key: 'AdaptiveInactivityDays', label: 'Inactivity before decay (days)', type: 'num' },
        { key: 'AdaptiveProbationDays', label: 'Probation length (days)', type: 'num' }
      ]);
      host.appendChild(adaptWrap);
      function syncAdapt() { adaptWrap.style.display = adaptEnable.checked ? '' : 'none'; }
      adaptEnable.addEventListener('change', syncAdapt); syncAdapt();
      var tail = cfgForm(host, cfg, [
        { type: 'section', label: 'Retention & cleanup' },
        { key: 'DeletionRetentionHours', label: 'Deletion retention (hours)', type: 'num' },
        { key: 'RemoveEmptySeries', label: 'Remove empty series left after deletion', type: 'check' },
        { key: 'EmptySeriesMinAgeHours', label: 'Empty-series grace period (hours)', type: 'num' },
        { key: 'MediaExpiryDays', label: 'Media ownership expiry (days)', type: 'num', hint: '0 disables expiry.' }
      ]);
      var adaptForm = { apply: function (live) { live.AdaptiveQuotaEnabled = adaptEnable.checked; } };
      host.appendChild(cfgSaveButton([reqForm, genresForm, quotaForm, adaptForm, adapt, tail]));
    });
  }

  function resultSpan() { var s = document.createElement('span'); s.className = 'jellycrowd-field-hint'; s.style.marginLeft = '.6em'; return s; }
  function withResult(btn, res) { var row = document.createElement('div'); row.className = 'jellycrowd-admin-actions'; row.appendChild(btn); row.appendChild(res); return row; }

  // POST a test endpoint (uses the SAVED config), showing ✅ / the server's error detail.
  function postTest(url, resultEl, okLabel) {
    resultEl.textContent = '…';
    var headers = {};
    if (window.ApiClient && window.ApiClient.accessToken) { headers.Authorization = 'MediaBrowser Token="' + window.ApiClient.accessToken() + '"'; }
    return fetch(pluginUrl(url), { method: 'POST', headers: headers }).then(function (r) {
      if (r.ok) { resultEl.textContent = '✅ ' + okLabel; return; }
      return r.text().then(function (body) { var msg = body; try { msg = JSON.parse(body).detail || body; } catch (e) { /* not JSON */ } resultEl.textContent = '❌ ' + (msg || ('HTTP ' + r.status)); });
    }).catch(function (e) { resultEl.textContent = '❌ ' + (e && e.message ? e.message : 'request failed'); });
  }

  function renderCfgNotifications(container) {
    cfgLoad(container, function (host, cfg) {
      var discord = cfgForm(host, cfg, [
        { type: 'section', label: 'Discord' },
        { key: 'DiscordWebhookUrl', label: 'Discord webhook URL', type: 'text', placeholder: 'https://discord.com/api/webhooks/…', hint: 'Empty disables Discord.' },
        { key: 'DiscordNotifyCreated', label: 'Notify on: created', type: 'check' },
        { key: 'DiscordNotifyApproved', label: 'Notify on: approved', type: 'check' },
        { key: 'DiscordNotifyDenied', label: 'Notify on: denied', type: 'check' },
        { key: 'DiscordNotifyAvailable', label: 'Notify on: available', type: 'check' },
        { key: 'DiscordColorCreated', label: 'Color: created', type: 'color' },
        { key: 'DiscordColorApproved', label: 'Color: approved', type: 'color' },
        { key: 'DiscordColorDenied', label: 'Color: denied', type: 'color' },
        { key: 'DiscordColorAvailable', label: 'Color: available', type: 'color' },
        { key: 'DiscordShowPoster', label: 'Show poster', type: 'check' },
        { key: 'DiscordShowSynopsis', label: 'Show synopsis', type: 'check' },
        { key: 'DiscordShowRequestedBy', label: 'Show "requested by"', type: 'check' },
        { key: 'DiscordShowStatus', label: 'Show status', type: 'check' },
        { key: 'DiscordShowSeason', label: 'Show season', type: 'check' },
        { key: 'DiscordShowLink', label: 'Link title to TMDB', type: 'check' },
        { key: 'DiscordMention', label: 'Mention / ping', type: 'text', placeholder: '<@&roleId>' }
      ]);
      var email = cfgForm(host, cfg, [
        { type: 'section', label: 'Email (SMTP)' },
        { key: 'SmtpHost', label: 'SMTP host', type: 'text', hint: 'Empty disables email.' },
        { key: 'SmtpPort', label: 'SMTP port', type: 'num' },
        { key: 'SmtpUseSsl', label: 'Use SSL/TLS', type: 'check' },
        { key: 'SmtpUsername', label: 'SMTP username', type: 'text' },
        { key: 'SmtpPassword', label: 'SMTP password', type: 'text' },
        { key: 'SmtpFromAddress', label: 'From address', type: 'text' },
        { key: 'NotificationEmailTo', label: 'Ops mailbox (recipient)', type: 'text' },
        { key: 'EmailNotifyCreated', label: 'Email ops on: created', type: 'check' },
        { key: 'EmailNotifyApproved', label: 'Email ops on: approved', type: 'check' },
        { key: 'EmailNotifyDenied', label: 'Email ops on: denied', type: 'check' },
        { key: 'EmailNotifyAvailable', label: 'Email ops on: available', type: 'check' },
        { key: 'SmtpAllowInvalidCertificate', label: 'Accept invalid/self-signed cert (insecure)', type: 'check' }
      ]);
      var channels = cfgForm(host, cfg, [
        { type: 'section', label: 'More channels' },
        { key: 'TelegramBotToken', label: 'Telegram bot token', type: 'text' },
        { key: 'TelegramChatId', label: 'Telegram chat id', type: 'text' },
        { key: 'NtfyServer', label: 'ntfy server', type: 'text', placeholder: 'https://ntfy.sh' },
        { key: 'NtfyTopic', label: 'ntfy topic', type: 'text' },
        { key: 'NtfyToken', label: 'ntfy token', type: 'text' },
        { key: 'GotifyServer', label: 'Gotify server', type: 'text' },
        { key: 'GotifyToken', label: 'Gotify token', type: 'text' },
        { key: 'PushoverToken', label: 'Pushover app token', type: 'text' },
        { key: 'PushoverUser', label: 'Pushover user key', type: 'text' },
        { key: 'SlackWebhookUrl', label: 'Slack webhook URL', type: 'text' },
        { key: 'NotifyWebhookUrl', label: 'Generic webhook URL', type: 'text' }
      ]);
      host.appendChild(cfgSaveButton([discord, email, channels]));
      host.appendChild(sectionHeading('Test (save first)'));
      var res = resultSpan();
      var row = document.createElement('div'); row.className = 'jellycrowd-admin-actions';
      [['Discord', 'discord'], ['Email', 'email'], ['Telegram', 'telegram'], ['ntfy', 'ntfy'], ['Gotify', 'gotify'], ['Pushover', 'pushover'], ['Slack', 'slack'], ['Webhook', 'webhook']].forEach(function (p) {
        row.appendChild(adminBtn(p[0], '', function () { postTest('JellyCrowd/Notifications/Test/' + p[1], res, p[1] + ' OK'); }));
      });
      row.appendChild(res);
      host.appendChild(row);
    });
  }

  // A Servarr resource <select> pre-seeded with its saved value (so a save preserves it before "Connect").
  function servarrSelect(saved, useName) {
    var s = document.createElement('select');
    s.className = 'jellycrowd-text-input';
    if (saved !== '' && saved != null) {
      var opt = document.createElement('option');
      opt.value = String(saved);
      opt.textContent = useName ? String(saved) : ('(saved id ' + saved + ')');
      opt.selected = true;
      s.appendChild(opt);
    }
    return s;
  }
  function fillSelect(select, items, saved, useName) {
    select.innerHTML = '';
    var found = false;
    (items || []).forEach(function (item) {
      var opt = document.createElement('option');
      opt.value = useName ? item.Name : String(item.Id);
      opt.textContent = item.Name;
      if (opt.value === String(saved)) { opt.selected = true; found = true; }
      select.appendChild(opt);
    });
    if (!found && saved !== '' && saved != null) {
      var o = document.createElement('option'); o.value = String(saved); o.textContent = useName ? String(saved) : ('(saved id ' + saved + ')'); o.selected = true; select.appendChild(o);
    }
  }
  function connectServarr(service, url, apiKey, resultEl, selects) {
    resultEl.textContent = '…';
    var headers = { 'Content-Type': 'application/json' };
    if (window.ApiClient && window.ApiClient.accessToken) { headers.Authorization = 'MediaBrowser Token="' + window.ApiClient.accessToken() + '"'; }
    fetch(pluginUrl('JellyCrowd/Download/Servarr/Resources'), { method: 'POST', headers: headers, body: JSON.stringify({ Service: service, Url: url, ApiKey: apiKey }) })
      .then(function (r) {
        if (!r.ok) { return r.text().then(function (body) { var msg = body; try { msg = JSON.parse(body).detail || body; } catch (e) { /* not JSON */ } resultEl.textContent = '❌ ' + (msg || ('HTTP ' + r.status)); }); }
        return r.json().then(function (res) {
          fillSelect(selects.root, res.RootFolders, selects.root.value, true);
          fillSelect(selects.profile, res.QualityProfiles, selects.profile.value, false);
          if (selects.lang) { fillSelect(selects.lang, res.LanguageProfiles, selects.lang.value, false); }
          resultEl.textContent = '✅ connected';
        });
      })
      .catch(function (e) { resultEl.textContent = '❌ ' + (e && e.message ? e.message : 'request failed'); });
  }

  function renderCfgDownload(container) {
    cfgLoad(container, function (host, cfg) {
      var backend = cfgForm(host, cfg, [
        { key: 'DownloadBackend', label: 'Download backend', type: 'select', options: [['none', 'None (manual admin queue)'], ['webhook', 'Webhook (POST to a URL)'], ['servarr', 'Radarr / Sonarr (Servarr)'], ['script', 'Local script']], hint: 'How approved requests are fulfilled.' }
      ]);
      var backendSel = host.querySelector('.jc-c-DownloadBackend');

      var webhookWrap = document.createElement('div');
      var webhook = cfgForm(webhookWrap, cfg, [
        { type: 'section', label: 'Webhook' },
        { key: 'DownloadWebhookUrl', label: 'Webhook URL', type: 'text' },
        { key: 'DownloadWebhookHeaders', label: 'Headers (one per line, Name: Value)', type: 'area' }
      ]);
      host.appendChild(webhookWrap);

      var servarrWrap = document.createElement('div');
      servarrWrap.appendChild(sectionHeading('Radarr (movies)'));
      var radarr = cfgForm(servarrWrap, cfg, [
        { key: 'RadarrUrl', label: 'Radarr URL', type: 'text', placeholder: 'http://localhost:7878' },
        { key: 'RadarrApiKey', label: 'Radarr API key', type: 'text' }
      ]);
      var radarrRoot = servarrSelect(cfg.RadarrRootFolderPath, true);
      servarrWrap.appendChild(field('Radarr root folder', radarrRoot));
      var radarrProfile = servarrSelect(cfg.RadarrQualityProfileId, false);
      servarrWrap.appendChild(field('Radarr quality profile', radarrProfile));
      var radarrRes = resultSpan();
      servarrWrap.appendChild(withResult(adminBtn('Connect Radarr', '', function () {
        connectServarr('radarr', host.querySelector('.jc-c-RadarrUrl').value, host.querySelector('.jc-c-RadarrApiKey').value, radarrRes, { root: radarrRoot, profile: radarrProfile });
      }), radarrRes));

      servarrWrap.appendChild(sectionHeading('Sonarr (shows)'));
      var sonarr = cfgForm(servarrWrap, cfg, [
        { key: 'SonarrUrl', label: 'Sonarr URL', type: 'text', placeholder: 'http://localhost:8989' },
        { key: 'SonarrApiKey', label: 'Sonarr API key', type: 'text' }
      ]);
      var sonarrRoot = servarrSelect(cfg.SonarrRootFolderPath, true);
      servarrWrap.appendChild(field('Sonarr root folder', sonarrRoot));
      var sonarrProfile = servarrSelect(cfg.SonarrQualityProfileId, false);
      servarrWrap.appendChild(field('Sonarr quality profile', sonarrProfile));
      var sonarrLang = servarrSelect(cfg.SonarrLanguageProfileId, false);
      servarrWrap.appendChild(field('Sonarr language profile', sonarrLang));
      var sonarrRes = resultSpan();
      servarrWrap.appendChild(withResult(adminBtn('Connect Sonarr', '', function () {
        connectServarr('sonarr', host.querySelector('.jc-c-SonarrUrl').value, host.querySelector('.jc-c-SonarrApiKey').value, sonarrRes, { root: sonarrRoot, profile: sonarrProfile, lang: sonarrLang });
      }), sonarrRes));

      servarrWrap.appendChild(sectionHeading('Prowlarr (optional)'));
      var prowlarr = cfgForm(servarrWrap, cfg, [
        { key: 'ProwlarrUrl', label: 'Prowlarr URL', type: 'text' },
        { key: 'ProwlarrApiKey', label: 'Prowlarr API key', type: 'text' }
      ]);
      servarrWrap.appendChild(sectionHeading('Stalled downloads'));
      var stalled = cfgForm(servarrWrap, cfg, [
        { key: 'RecoverStalledDownloads', label: 'Auto-recover stalled downloads', type: 'check' },
        { key: 'StalledRecoveryMinutes', label: 'Stalled after (minutes)', type: 'num' }
      ]);
      host.appendChild(servarrWrap);

      var scriptWrap = document.createElement('div');
      var script = cfgForm(scriptWrap, cfg, [
        { type: 'section', label: 'Local script' },
        { key: 'ScriptPath', label: 'Script path', type: 'text' },
        { key: 'ScriptArguments', label: 'Arguments', type: 'text' }
      ]);
      host.appendChild(scriptWrap);

      var servarrSelectsForm = { apply: function (live) {
        live.RadarrRootFolderPath = radarrRoot.value;
        live.RadarrQualityProfileId = parseInt(radarrProfile.value || '0', 10);
        live.SonarrRootFolderPath = sonarrRoot.value;
        live.SonarrQualityProfileId = parseInt(sonarrProfile.value || '0', 10);
        live.SonarrLanguageProfileId = parseInt(sonarrLang.value || '0', 10);
      } };
      host.appendChild(cfgSaveButton([backend, webhook, radarr, sonarr, prowlarr, stalled, script, servarrSelectsForm]));

      host.appendChild(sectionHeading('Test (save first)'));
      var testRes = resultSpan();
      host.appendChild(withResult(adminBtn('Test backend', '', function () { postTest('JellyCrowd/Download/Test', testRes, 'OK'); }), testRes));

      function syncVis() {
        var b = backendSel.value;
        webhookWrap.style.display = b === 'webhook' ? '' : 'none';
        servarrWrap.style.display = b === 'servarr' ? '' : 'none';
        scriptWrap.style.display = b === 'script' ? '' : 'none';
      }
      backendSel.addEventListener('change', syncVis); syncVis();
    });
  }

  function renderCfgDiagnostics(container) {
    container.innerHTML = '';
    setMessage('');
    container.appendChild(sectionHeading('Diagnostics'));
    var actions = document.createElement('div'); actions.className = 'jellycrowd-admin-actions';
    var diagRes = document.createElement('div'); diagRes.style.marginTop = '.6em';
    actions.appendChild(adminBtn('Run checks', '', function () { runDiagnostics(diagRes); }));
    actions.appendChild(adminBtn('Download backup', '', function () { downloadBackup(); }));
    container.appendChild(actions);
    container.appendChild(diagRes);
    runDiagnostics(diagRes);

    container.appendChild(sectionHeading('Library cleanup'));
    var orphans = checkbox('jc-cleanup-orphans', false);
    container.appendChild(field('Orphans only (0 owners)', orphans));
    var scanRow = document.createElement('div'); scanRow.className = 'jellycrowd-admin-actions';
    var cleanupRes = document.createElement('div'); cleanupRes.style.marginTop = '.6em';
    scanRow.appendChild(adminBtn('Scan media', '', function () { scanCleanup(orphans.checked, cleanupRes); }));
    container.appendChild(scanRow);
    container.appendChild(cleanupRes);
  }

  function runDiagnostics(box) {
    box.textContent = '…';
    apiGet('JellyCrowd/Diagnostics').then(function (rows) {
      box.innerHTML = '';
      var icons = { ok: '✅', warning: '⚠️', error: '❌', info: 'ℹ️' };
      (rows || []).forEach(function (r) {
        var row = document.createElement('div');
        row.style.cssText = 'display:flex;gap:.6em;padding:.4em 0;border-bottom:1px solid rgba(127,127,127,.2);align-items:baseline;';
        var name = document.createElement('span'); name.style.cssText = 'font-weight:600;min-width:15em;flex:0 0 auto;'; name.textContent = (icons[r.Status] || '•') + ' ' + r.Name;
        var detail = document.createElement('span'); detail.style.cssText = 'opacity:.85;flex:1 1 auto;min-width:0;'; detail.textContent = r.Detail;
        row.appendChild(name); row.appendChild(detail); box.appendChild(row);
      });
    }).catch(function () { box.textContent = t('error_generic'); });
  }

  function scanCleanup(orphansOnly, box) {
    box.textContent = '…';
    apiGet('JellyCrowd/Maintenance/Media?orphansOnly=' + (orphansOnly ? 'true' : 'false')).then(function (items) {
      box.innerHTML = '';
      if (!items || !items.length) { box.textContent = 'No matching media.'; return; }
      items.forEach(function (m) {
        var row = document.createElement('div');
        row.style.cssText = 'display:flex;gap:.6em;padding:.4em 0;border-bottom:1px solid rgba(127,127,127,.2);align-items:center;';
        var label = m.Title + (m.Season != null ? ' — Season ' + m.Season : '');
        var name = document.createElement('span'); name.style.cssText = 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
        name.textContent = (m.MediaType === 'tv' ? '📺 ' : '🎬 ') + label + ' — ' + fmtBytes(m.SizeBytes) + ' · ' + m.OwnerCount + ' owner(s)';
        var del = adminBtn('Delete', '', function (btn) {
          if (!window.confirm('Delete "' + label + '" from disk? This is permanent.')) { return; }
          btn.disabled = true;
          apiPostNoResult('JellyCrowd/Maintenance/Media/' + m.JellyfinItemId + '/Delete').then(function () { row.remove(); }).catch(function () { btn.disabled = false; });
        });
        row.appendChild(name); row.appendChild(del); box.appendChild(row);
      });
    }).catch(function () { box.textContent = t('error_generic'); });
  }

  function downloadBackup() {
    var headers = {};
    if (window.ApiClient && window.ApiClient.accessToken) { headers.Authorization = 'MediaBrowser Token="' + window.ApiClient.accessToken() + '"'; }
    fetch(pluginUrl('JellyCrowd/Diagnostics/Export'), { headers: headers }).then(function (r) { return r.blob(); }).then(function (blob) {
      var url = URL.createObjectURL(blob); var a = document.createElement('a'); a.href = url; a.download = 'jellycrowd-backup.json'; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    }).catch(function () { /* ignore */ });
  }

  function fmtBytes(n) { n = Number(n) || 0; var u = ['B', 'KiB', 'MiB', 'GiB', 'TiB']; var i = 0; while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; } return (i === 0 ? n : n.toFixed(1)) + ' ' + u[i]; }

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
    [['', t('admin_filter_all')], ['request', 'request'], ['download', 'download'], ['report', 'report'], ['admin', 'admin'], ['user', 'user'], ['system', 'system']]
      .forEach(function (o) { var x = document.createElement('option'); x.value = o[0]; x.textContent = o[1]; cat.appendChild(x); });
    var level = document.createElement('select');
    [['', t('admin_filter_all')], ['info', 'info'], ['warning', 'warning'], ['error', 'error']]
      .forEach(function (o) { var x = document.createElement('option'); x.value = o[0]; x.textContent = o[1]; level.appendChild(x); });
    // User filter — options are filled from the actors present in recent activity (one unfiltered read).
    var user = document.createElement('select');
    var userAll = document.createElement('option'); userAll.value = ''; userAll.textContent = t('log_user_all'); user.appendChild(userAll);
    bar.appendChild(search);
    bar.appendChild(cat);
    bar.appendChild(level);
    bar.appendChild(user);
    container.appendChild(bar);

    var box = document.createElement('div');
    box.className = 'jellycrowd-logs';
    container.appendChild(box);

    apiGet('JellyCrowd/Logs?limit=500').then(function (rows) {
      var seen = {};
      (rows || []).forEach(function (r) { if (r.User) { seen[r.User] = 1; } });
      Object.keys(seen).sort(function (a, b) { return a.toLowerCase().localeCompare(b.toLowerCase()); })
        .forEach(function (u) { var o = document.createElement('option'); o.value = u; o.textContent = u; user.appendChild(o); });
    }).catch(function () { /* keep just "all users" on failure */ });

    function load() {
      box.textContent = t('loading');
      var qs = 'term=' + encodeURIComponent(search.value || '') + '&category=' + encodeURIComponent(cat.value || '')
        + '&level=' + encodeURIComponent(level.value || '') + '&user=' + encodeURIComponent(user.value || '') + '&limit=200';
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
          row.appendChild(when);
          row.appendChild(c);
          if (r.User) {
            var us = document.createElement('span');
            us.className = 'jellycrowd-log-user';
            us.style.cssText = 'opacity:.85;white-space:nowrap;';
            us.textContent = '👤 ' + r.User;
            row.appendChild(us);
          }
          var m = document.createElement('span');
          m.textContent = r.Message;
          row.appendChild(m);
          box.appendChild(row);
        });
      }).catch(function () { box.textContent = t('error_generic'); });
    }

    var deb;
    search.addEventListener('input', function () { clearTimeout(deb); deb = setTimeout(load, 250); });
    cat.addEventListener('change', load);
    level.addEventListener('change', load);
    user.addEventListener('change', load);
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
    function adminDeleteMedia(id) { apiPostNoResult('JellyCrowd/Requests/' + id + '/DeleteMedia').then(reload).catch(function () {}); }
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
      all.forEach(function (request) { tbody.appendChild(requestRow(request, decide, adminEdit, adminDelete, adminDeleteMedia)); });
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

  function requestRow(request, decide, adminEdit, adminDelete, adminDeleteMedia) {
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
    // Two distinct destructive actions: "Delete media" flags the actual files for removal (via the
    // retention task); "Remove request" only drops the plugin's request record and keeps the files.
    if (isAvailable && !request.DeletionRequestedAt) {
      tdA.appendChild(adminBtn(t('admin_delete_media'), 'danger', function () {
        if (window.confirm(t('confirm_delete_media'))) { adminDeleteMedia(request.Id); }
      }));
    } else if (request.DeletionRequestedAt) {
      var pend = document.createElement('span');
      pend.className = 'jellycrowd-status jellycrowd-status-denied';
      pend.textContent = t('deletion_pending');
      tdA.appendChild(pend);
    }
    tdA.appendChild(adminBtn(t('admin_remove_request'), 'danger', function () { adminDelete(request.Id); }));
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
      var fontOptions = [['', 'Default (Jellyfin)'], ['system-ui, sans-serif', 'System'], ['Inter, sans-serif', 'Inter'], ['Roboto, sans-serif', 'Roboto'], ['"Open Sans", sans-serif', 'Open Sans'], ['Lato, sans-serif', 'Lato'], ['Montserrat, sans-serif', 'Montserrat'], ['Poppins, sans-serif', 'Poppins'], ['Nunito, sans-serif', 'Nunito'], ['Georgia, serif', 'Georgia (serif)'], ['"Courier New", monospace', 'Courier (mono)']];
      var fontVal = b.BrandingFontFamily || '';
      if (fontVal && !fontOptions.some(function (o) { return o[0] === fontVal; })) { fontOptions.push([fontVal, fontVal + ' (current)']); }
      var fontFamily = selectInput('jc-b-font', fontVal, fontOptions);
      container.appendChild(field(t('branding_font'), fontFamily));
      // The font stylesheet URL is rendered lower down, just above Custom CSS (kept here only as a variable).
      var fontUrl = textInput('jc-b-fonturl', b.BrandingFontUrl || '', 'https://fonts.googleapis.com/…');

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

      container.appendChild(sectionHeading('Header icons'));
      var discEnable = checkbox('jc-b-disc-en', b.DiscordInviteEnabled === true);
      container.appendChild(field('Show Discord icon in the header', discEnable));
      var discUrl = textInput('jc-b-disc-url', b.DiscordInviteUrl || '', 'https://discord.gg/…');
      container.appendChild(field('Discord invite link', discUrl));
      var supEnable = checkbox('jc-b-sup-en', b.SupportLinkEnabled === true);
      container.appendChild(field('Show support / donation icon in the header', supEnable));
      var supUrl = textInput('jc-b-sup-url', b.SupportLinkUrl || '', 'https://…');
      container.appendChild(field('Support / donation link', supUrl));
      var guideEnable = checkbox('jc-b-guide-en', b.GuideLinkEnabled === true);
      container.appendChild(field('Show user-guide icon (?) in the header', guideEnable));
      var guideUrl = textInput('jc-b-guide-url', b.GuideLinkUrl || '', 'https://…');
      container.appendChild(field('User-guide link', guideUrl));

      container.appendChild(sectionHeading('Local intros (pre-roll)'));
      var liEnable = checkbox('jc-b-li-en', b.LocalIntrosEnabled === true);
      container.appendChild(field('Play a pre-roll before content', liEnable, 'Drop video(s) in a folder with the name below beside your media libraries; the plugin finds and indexes it. On the web client the pre-roll is non-skippable with hidden controls.'));
      var liFolder = textInput('jc-b-li-folder', b.LocalIntrosFolderName || 'intros', 'intros');
      container.appendChild(field('Pre-roll folder name', liFolder));
      var liMovies = checkbox('jc-b-li-mov', b.LocalIntrosOnMovies !== false);
      container.appendChild(field('Before movies', liMovies));
      var liFirst = checkbox('jc-b-li-first', b.LocalIntrosOnFirstEpisode !== false);
      container.appendChild(field('Before the first episode of a series (S01E01)', liFirst));
      var liNonSkip = checkbox('jc-b-li-ns', b.LocalIntrosNonSkippable !== false);
      container.appendChild(field('Non-skippable (web client)', liNonSkip));
      var liWebOnly = checkbox('jc-b-li-webonly', b.LocalIntrosWebOnly !== false);
      container.appendChild(field('Web & desktop players only (recommended)', liWebOnly, 'Native mobile and TV apps can fail to start playback when a pre-roll is prepended (reported on iPad). When on, only the web client and Jellyfin Media Player receive intros.'));

      container.appendChild(field(t('branding_font_url'), fontUrl, t('branding_font_url_hint')));

      container.appendChild(sectionHeading(t('branding_custom_css')));
      var cssNote = document.createElement('p');
      cssNote.className = 'jellycrowd-field-hint';
      cssNote.textContent = "Injected after Jellyfin's own styles, so your rules override the default appearance (last one wins).";
      container.appendChild(cssNote);
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
          live.DiscordInviteEnabled = discEnable.checked;
          live.DiscordInviteUrl = discUrl.value.trim();
          live.SupportLinkEnabled = supEnable.checked;
          live.SupportLinkUrl = supUrl.value.trim();
          live.GuideLinkEnabled = guideEnable.checked;
          live.GuideLinkUrl = guideUrl.value.trim();
          live.LocalIntrosEnabled = liEnable.checked;
          live.LocalIntrosFolderName = liFolder.value.trim() || 'intros';
          live.LocalIntrosOnMovies = liMovies.checked;
          live.LocalIntrosOnFirstEpisode = liFirst.checked;
          live.LocalIntrosNonSkippable = liNonSkip.checked;
          live.LocalIntrosWebOnly = liWebOnly.checked;
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

  // ---------- Reviews moderation (sub-tab of Moderation) ----------
  function renderReviews(container) {
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
