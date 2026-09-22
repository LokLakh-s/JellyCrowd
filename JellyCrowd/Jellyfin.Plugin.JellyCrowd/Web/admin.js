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
    // Keep the labels already in hand when the catalog cannot be read: replacing them with an empty
    // object turns every button in the view into its raw key.
    return lib.fetchStrings(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (loaded) { if (loaded) { strings = loaded; } });
  }

  function setMessage(text) {
    var el = document.getElementById('jcAdminMessage');
    if (!el) { return; }
    if (text) { el.textContent = text; el.hidden = false; } else { el.hidden = true; }
  }

  // Asks the question in our own dialog rather than window.confirm(), which cannot be styled, cannot be
  // translated, and on mobile arrives as a system alert detached from what the admin was doing.
  // Resolves true only when the confirm button is used: Escape, the cancel button and a click outside
  // all mean no. Focus moves into the dialog, stays trapped there and returns to the trigger on close.
  function confirmAction(opts) {
    return new Promise(function (resolve) {
      var d = lib.buildConfirmDialog(document, {
        title: opts.title,
        message: opts.message,
        confirmLabel: opts.confirmLabel,
        cancelLabel: t('cancel'),
        danger: opts.danger === true
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
      // Capture phase: the overlay's own Escape handler would otherwise close the whole panel behind us.
      document.addEventListener('keydown', onKey, true);

      document.body.appendChild(d.root);
      lib.focusFirst(d.root);
    });
  }

  // The admin tabs, ordered by how often an admin uses them (most-used first). Some tabs group related
  // views under sub-tabs (see subTabs): Moderation = Reports + Reviews; Users = Per-user + Ownership.
  // `render(container)` fills the content area for that tab.
  var TABS = [
    { id: 'requests', labelKey: 'tab_requests', render: renderRequests },
    { id: 'stats', labelKey: 'tab_stats', render: renderStats },
    { id: 'moderation', labelKey: 'nav_moderation', render: renderModeration },
    { id: 'polls', labelKey: 'tab_polls', render: renderPolls },
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
      { id: 'groups', labelKey: 'tab_groups', render: renderGroups },
      { id: 'ownership', labelKey: 'nav_ownership', render: renderOwnership },
      { id: 'assign', labelKey: 'tab_assign_media', render: renderAssignMedia }
    ]);
  }

  // Assign ownership of a title already in the library to a user (e.g. a manual download nobody requested).
  function renderAssignMedia(container) {
    setMessage(t('loading'));
    var usersPromise = (window.ApiClient && window.ApiClient.getUsers) ? window.ApiClient.getUsers() : Promise.resolve([]);
    Promise.all([usersPromise, apiGet('JellyCrowd/Maintenance/Media?orphansOnly=false')])
      .then(function (res) {
        var users = res[0] || [];
        var media = res[1] || [];
        container.innerHTML = '';

        var sub = document.createElement('p');
        sub.className = 'jellycrowd-disclaimer';
        sub.textContent = t('assign_media_subtitle');
        container.appendChild(sub);

        var filter = document.createElement('input');
        filter.type = 'search';
        filter.className = 'jellycrowd-search-input';
        filter.placeholder = t('assign_media_filter');
        container.appendChild(filter);

        var list = document.createElement('div');
        list.className = 'jellycrowd-list';
        container.appendChild(list);

        function label(it) {
          return (it.MediaType === 'tv' ? '📺 ' : '🎬 ') + it.Title
            + (it.Season != null ? ' — ' + t('season_number').replace('{n}', it.Season) : '');
        }

        function row(it) {
          var r = document.createElement('div');
          r.className = 'jellycrowd-assign-row';

          var name = document.createElement('span');
          name.className = 'jellycrowd-assign-title';
          name.textContent = label(it);
          r.appendChild(name);

          var owners = document.createElement('span');
          owners.className = 'jellycrowd-status jellycrowd-owners';
          owners.textContent = t('assign_owned_by').replace('{n}', it.OwnerCount || 0);
          r.appendChild(owners);

          var select = document.createElement('select');
          select.className = 'jellycrowd-assign-select';
          var ph = document.createElement('option');
          ph.value = '';
          ph.textContent = t('assign_choose_user');
          select.appendChild(ph);
          users.forEach(function (u) {
            var o = document.createElement('option');
            o.value = u.Id;
            o.textContent = u.Name;
            select.appendChild(o);
          });
          r.appendChild(select);

          var btn = adminBtn(t('assign_button'), '', function (b) {
            var uid = select.value;
            if (!uid) { return; }
            b.disabled = true;
            apiPostJson('JellyCrowd/Requests/AssignOwner', {
              UserId: uid,
              TmdbId: it.TmdbId,
              MediaType: it.MediaType,
              Title: it.Title,
              Season: it.Season
            })
              .then(function () {
                setMessage(t('assign_done').replace('{user}', select.options[select.selectedIndex].textContent));
                it.OwnerCount = (it.OwnerCount || 0) + 1;
                owners.textContent = t('assign_owned_by').replace('{n}', it.OwnerCount);
                select.value = '';
                b.disabled = false;
              })
              .catch(function () { setMessage(t('assign_failed')); b.disabled = false; });
          });
          r.appendChild(btn);
          return r;
        }

        function paint() {
          var q = (filter.value || '').trim().toLowerCase();
          var shown = media.filter(function (it) { return !q || (it.Title || '').toLowerCase().indexOf(q) >= 0; });
          list.innerHTML = '';
          if (!shown.length) { setMessage(q ? t('no_results') : t('assign_media_empty')); return; }
          setMessage('');
          shown.forEach(function (it) { list.appendChild(row(it)); });
        }

        filter.addEventListener('input', paint);
        paint();
      })
      .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
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
  // spec: { key, label, type:'text'|'secret'|'area'|'secretArea'|'num'|'check'|'select'|'section', hint, placeholder, options, scale }
  // 'secret' / 'secretArea' mask a password, API key, token or webhook URL until the admin reveals it.
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
      else if (s.type === 'secret') {
        vc = textInput(cls, v != null ? v : '', s.placeholder);
        ctrl = lib.maskSecret(vc, { show: t('adm_secret_show'), hide: t('adm_secret_hide') });
      }
      else if (s.type === 'secretArea') {
        vc = document.createElement('textarea'); vc.className = cls + ' jellycrowd-text-input'; vc.rows = s.rows || 3; if (v != null) { vc.value = v; }
        ctrl = lib.maskSecret(vc, { show: t('adm_secret_show'), hide: t('adm_secret_hide') });
      }
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
        { key: 'Language', label: t('filters_language'), type: 'select', options: [['auto', t('adm_opt_auto_language')], ['en', 'English'], ['fr', 'Français']], hint: t('cfg_language_hint') },
        { key: 'HiddenFromUsers', label: t('cfg_hiddenfromusers'), type: 'check', hint: t('cfg_hiddenfromusers_hint') },
        { key: 'RateLimitPerMinute', label: t('cfg_ratelimitperminute'), type: 'num', hint: t('cfg_ratelimitperminute_hint') },
        { key: 'RateLimitGetPerMinute', label: t('cfg_ratelimitgetperminute'), type: 'num', hint: t('cfg_ratelimitgetperminute_hint') },
        { key: 'CommentsEnabled', label: t('cfg_commentsenabled'), type: 'check' },
        { key: 'ShowReviewAuthors', label: t('cfg_showreviewauthors'), type: 'check' },
        { key: 'SkipOutroEnabled', label: t('cfg_skipoutroenabled'), type: 'check' },
        { key: 'SkipIntroEnabled', label: t('cfg_skipintroenabled'), type: 'check' },
        { key: 'SegmentMinSeasonCoveragePercent', label: t('cfg_segmentminseasoncoverage'), type: 'num', hint: t('cfg_segmentminseasoncoverage_hint') },
        { key: 'SegmentHwAccel', label: t('cfg_segmenthwaccel'), type: 'select', options: [['auto', t('adm_opt_auto_gpu')], ['none', t('adm_opt_cpu_only')], ['vaapi', 'Intel / AMD (VAAPI)'], ['qsv', 'Intel QuickSync (QSV)'], ['cuda', 'NVIDIA (CUDA)'], ['videotoolbox', 'macOS (VideoToolbox)']], hint: t('cfg_segmenthwaccel_hint') },
        { key: 'TmdbApiKey', label: t('cfg_tmdbapikey'), type: 'secret', placeholder: t('adm_ph_tmdb_key') }
      ]);
      host.appendChild(cfgSaveButton([form]));
    });
  }

  function renderCfgRequests(container) {
    cfgLoad(container, function (host, cfg) {
      var reqForm = cfgForm(host, cfg, [
        { type: 'section', label: t('tab_requests') },
        { key: 'RequireApproval', label: t('cfg_requireapproval'), type: 'check' },
        { key: 'AllowUserRetrySearch', label: t('cfg_allowuserretrysearch'), type: 'check' },
        { key: 'MaxRequestsPerPeriod', label: t('cfg_maxrequestsperperiod'), type: 'num', hint: t('cfg_maxrequestsperperiod_hint') },
        { key: 'RequestPeriod', label: t('cfg_requestperiod'), type: 'select', options: [['Day', t('calendar_view_day')], ['Week', t('calendar_view_week')], ['Month', t('calendar_view_month')]] },
        { key: 'AutoApproveMaxSizeBytes', label: t('cfg_autoapprovemaxsizebytes'), type: 'num', scale: GIB, hint: t('cfg_autoapprovemaxsizebytes_hint') },
        { key: 'EstimatedMovieSizeBytes', label: t('cfg_estimatedmoviesizebytes'), type: 'num', scale: GIB },
        { key: 'EstimatedEpisodeSizeBytes', label: t('cfg_estimatedepisodesizebytes'), type: 'num', scale: GIB }
      ]);
      // Request scope: which media types are offered, and which TV request granularities are allowed.
      host.appendChild(sectionHeading(t('cfgsec_request_scope')));
      var mvEnabled = checkbox('jc-c-MoviesEnabled', cfg.MoviesEnabled !== false);
      var srEnabled = checkbox('jc-c-SeriesEnabled', cfg.SeriesEnabled !== false);
      host.appendChild(field(t('cfg_movies_enabled'), mvEnabled));
      host.appendChild(field(t('cfg_series_enabled'), srEnabled, t('cfg_media_types_hint')));
      keepAtLeastOne([mvEnabled, srEnabled]);
      var gSeries = checkbox('jc-c-AllowSeriesRequests', cfg.AllowSeriesRequests !== false);
      var gSeason = checkbox('jc-c-AllowSeasonRequests', cfg.AllowSeasonRequests !== false);
      var gEpisode = checkbox('jc-c-AllowEpisodeRequests', cfg.AllowEpisodeRequests !== false);
      host.appendChild(field(t('cfg_allow_series_requests'), gSeries));
      host.appendChild(field(t('cfg_allow_season_requests'), gSeason));
      host.appendChild(field(t('cfg_allow_episode_requests'), gEpisode, t('cfg_request_granularity_hint')));
      keepAtLeastOne([gSeries, gSeason, gEpisode]);
      var scopeForm = { apply: function (live) {
        live.MoviesEnabled = mvEnabled.checked;
        live.SeriesEnabled = srEnabled.checked;
        live.AllowSeriesRequests = gSeries.checked;
        live.AllowSeasonRequests = gSeason.checked;
        live.AllowEpisodeRequests = gEpisode.checked;
      } };

      var genresInput = textInput('jc-c-AutoApproveGenres', (cfg.AutoApproveGenres || []).join(', '), 'Action, Comedy, Documentary…');
      host.appendChild(field(t('adm_auto_approve_genres_optional'), genresInput, t('adm_comma_separated_tmdb_genre_names_when_hint')));
      var genresForm = { apply: function (live) { live.AutoApproveGenres = genresInput.value.split(',').map(function (g) { return g.trim(); }).filter(Boolean); } };
      var quotaForm = cfgForm(host, cfg, [
        { type: 'section', label: t('cfgsec_quotas') },
        { key: 'DefaultUserQuotaBytes', label: t('cfg_defaultuserquotabytes'), type: 'num', scale: GIB, hint: t('cfg_defaultuserquotabytes_hint') }
      ]);
      var adaptEnable = checkbox('jc-c-AdaptiveQuotaEnabled', cfg.AdaptiveQuotaEnabled === true);
      host.appendChild(field(t('adm_adaptive_quota_reward_active_users'), adaptEnable, t('adm_elevate_active_users_above_and_decay_hint')));
      var adaptWrap = document.createElement('div');
      var adapt = cfgForm(adaptWrap, cfg, [
        { key: 'AdaptiveFloorPercent', label: t('cfg_adaptivefloorpercent'), type: 'num' },
        { key: 'AdaptiveCeilingPercent', label: t('cfg_adaptiveceilingpercent'), type: 'num' },
        { key: 'AdaptiveWindowDays', label: t('cfg_adaptivewindowdays'), type: 'num' },
        { key: 'AdaptiveMinMinutes', label: t('cfg_adaptiveminminutes'), type: 'num' },
        { key: 'AdaptiveMinActiveDays', label: t('cfg_adaptiveminactivedays'), type: 'num' },
        { key: 'AdaptiveInactivityDays', label: t('cfg_adaptiveinactivitydays'), type: 'num' },
        { key: 'AdaptiveProbationDays', label: t('cfg_adaptiveprobationdays'), type: 'num' }
      ]);
      host.appendChild(adaptWrap);
      function syncAdapt() { adaptWrap.style.display = adaptEnable.checked ? '' : 'none'; }
      adaptEnable.addEventListener('change', syncAdapt); syncAdapt();
      var tail = cfgForm(host, cfg, [
        { type: 'section', label: t('cfgsec_retention_and_cleanup') },
        { key: 'DeletionRetentionHours', label: t('cfg_deletionretentionhours'), type: 'num' },
        { key: 'RemoveEmptySeries', label: t('cfg_removeemptyseries'), type: 'check' },
        { key: 'EmptySeriesMinAgeHours', label: t('cfg_emptyseriesminagehours'), type: 'num' },
        { key: 'MediaExpiryDays', label: t('cfg_mediaexpirydays'), type: 'num', hint: t('cfg_mediaexpirydays_hint') },
        { key: 'PartialAvailabilityGraceHours', label: t('cfg_partialavailabilitygracehours'), type: 'num', hint: t('cfg_partialavailabilitygracehours_hint') }
      ]);
      var adaptForm = { apply: function (live) { live.AdaptiveQuotaEnabled = adaptEnable.checked; } };
      host.appendChild(cfgSaveButton([reqForm, scopeForm, genresForm, quotaForm, adaptForm, adapt, tail]));
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
        { type: 'section', label: t('cfgsec_discord') },
        { key: 'DiscordWebhookUrl', label: t('cfg_discordwebhookurl'), type: 'secret', placeholder: 'https://discord.com/api/webhooks/…', hint: t('cfg_discordwebhookurl_hint') },
        { key: 'DiscordNotifyCreated', label: t('cfg_discordnotifycreated'), type: 'check' },
        { key: 'DiscordNotifyApproved', label: t('cfg_discordnotifyapproved'), type: 'check' },
        { key: 'DiscordNotifyDenied', label: t('cfg_discordnotifydenied'), type: 'check' },
        { key: 'DiscordNotifyAvailable', label: t('cfg_discordnotifyavailable'), type: 'check' },
        { key: 'DiscordNotifyReports', label: t('cfg_discordnotifyreports'), type: 'check' },
        { key: 'DiscordColorCreated', label: t('cfg_discordcolorcreated'), type: 'color' },
        { key: 'DiscordColorApproved', label: t('cfg_discordcolorapproved'), type: 'color' },
        { key: 'DiscordColorDenied', label: t('cfg_discordcolordenied'), type: 'color' },
        { key: 'DiscordColorAvailable', label: t('cfg_discordcoloravailable'), type: 'color' },
        { key: 'DiscordShowPoster', label: t('cfg_discordshowposter'), type: 'check' },
        { key: 'DiscordShowSynopsis', label: t('cfg_discordshowsynopsis'), type: 'check' },
        { key: 'DiscordShowRequestedBy', label: t('cfg_discordshowrequestedby'), type: 'check' },
        { key: 'DiscordShowStatus', label: t('cfg_discordshowstatus'), type: 'check' },
        { key: 'DiscordShowSeason', label: t('cfg_discordshowseason'), type: 'check' },
        { key: 'DiscordShowLink', label: t('cfg_discordshowlink'), type: 'check' },
        { key: 'DiscordMention', label: t('cfg_discordmention'), type: 'text', placeholder: '<@&roleId>' }
      ]);
      var email = cfgForm(host, cfg, [
        { type: 'section', label: t('cfgsec_email_smtp') },
        { key: 'SmtpHost', label: t('cfg_smtphost'), type: 'text', hint: t('cfg_smtphost_hint') },
        { key: 'SmtpPort', label: t('cfg_smtpport'), type: 'num' },
        { key: 'SmtpUseSsl', label: t('cfg_smtpusessl'), type: 'check' },
        { key: 'SmtpUsername', label: t('cfg_smtpusername'), type: 'text' },
        { key: 'SmtpPassword', label: t('cfg_smtppassword'), type: 'secret' },
        { key: 'SmtpFromAddress', label: t('cfg_smtpfromaddress'), type: 'text' },
        { key: 'NotificationEmailTo', label: t('cfg_notificationemailto'), type: 'text' },
        { key: 'EmailNotifyCreated', label: t('cfg_emailnotifycreated'), type: 'check' },
        { key: 'EmailNotifyApproved', label: t('cfg_emailnotifyapproved'), type: 'check' },
        { key: 'EmailNotifyDenied', label: t('cfg_emailnotifydenied'), type: 'check' },
        { key: 'EmailNotifyAvailable', label: t('cfg_emailnotifyavailable'), type: 'check' },
        { key: 'EmailNotifyReports', label: t('cfg_emailnotifyreports'), type: 'check' },
        { key: 'SmtpAllowInvalidCertificate', label: t('cfg_smtpallowinvalidcertificate'), type: 'check' }
      ]);
      var channels = cfgForm(host, cfg, [
        { type: 'section', label: t('cfgsec_more_channels') },
        { key: 'TelegramBotToken', label: t('cfg_telegrambottoken'), type: 'secret' },
        { key: 'TelegramChatId', label: t('cfg_telegramchatid'), type: 'text' },
        { key: 'NtfyServer', label: t('cfg_ntfyserver'), type: 'text', placeholder: 'https://ntfy.sh' },
        { key: 'NtfyTopic', label: t('notif_ntfy_topic'), type: 'secret' },
        { key: 'NtfyToken', label: t('cfg_ntfytoken'), type: 'secret' },
        { key: 'GotifyServer', label: t('cfg_gotifyserver'), type: 'text' },
        { key: 'GotifyToken', label: t('cfg_gotifytoken'), type: 'secret' },
        { key: 'PushoverToken', label: t('cfg_pushovertoken'), type: 'secret' },
        { key: 'PushoverUser', label: t('cfg_pushoveruser'), type: 'secret' },
        { key: 'SlackWebhookUrl', label: t('cfg_slackwebhookurl'), type: 'secret' },
        { key: 'NotifyWebhookUrl', label: t('cfg_notifywebhookurl'), type: 'secret' }
      ]);
      var reports = cfgForm(host, cfg, [
        { type: 'section', label: t('cfgsec_reports') },
        { key: 'ReportNotifyBell', label: t('cfg_reportnotifybell'), type: 'check', hint: t('cfg_reportnotifybell_hint') },
        { key: 'ReportNotifyOtherChannels', label: t('cfg_reportnotifyotherchannels'), type: 'check' },
        { key: 'ReportReminderDays', label: t('cfg_reportreminderdays'), type: 'num', hint: t('cfg_reportreminderdays_hint') }
      ]);
      host.appendChild(cfgSaveButton([discord, email, channels, reports]));
      host.appendChild(sectionHeading(t('adm_test_save_first')));
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
          resultEl.textContent = '✅ ' + t('adm_connected');
        });
      })
      .catch(function (e) { resultEl.textContent = '❌ ' + (e && e.message ? e.message : 'request failed'); });
  }

  function renderCfgDownload(container) {
    cfgLoad(container, function (host, cfg) {
      var backend = cfgForm(host, cfg, [
        { key: 'DownloadBackend', label: t('cfg_downloadbackend'), type: 'select', options: [['none', t('adm_opt_none_manual')], ['webhook', t('adm_opt_webhook')], ['servarr', 'Radarr / Sonarr (Servarr)'], ['script', t('adm_opt_local_script')]], hint: t('cfg_downloadbackend_hint') }
      ]);
      var backendSel = host.querySelector('.jc-c-DownloadBackend');

      var webhookWrap = document.createElement('div');
      var webhook = cfgForm(webhookWrap, cfg, [
        { type: 'section', label: t('cfgsec_webhook') },
        { key: 'DownloadWebhookUrl', label: t('cfg_downloadwebhookurl'), type: 'secret' },
        { key: 'DownloadWebhookHeaders', label: t('cfg_downloadwebhookheaders'), type: 'secretArea' }
      ]);
      host.appendChild(webhookWrap);

      var servarrWrap = document.createElement('div');
      servarrWrap.appendChild(sectionHeading(t('adm_radarr_movies')));
      var radarr = cfgForm(servarrWrap, cfg, [
        { key: 'RadarrUrl', label: t('cfg_radarrurl'), type: 'text', placeholder: 'http://localhost:7878' },
        { key: 'RadarrApiKey', label: t('cfg_radarrapikey'), type: 'secret' }
      ]);
      var radarrRoot = servarrSelect(cfg.RadarrRootFolderPath, true);
      servarrWrap.appendChild(field(t('adm_radarr_root_folder'), radarrRoot));
      var radarrProfile = servarrSelect(cfg.RadarrQualityProfileId, false);
      servarrWrap.appendChild(field(t('adm_radarr_quality_profile'), radarrProfile));
      var radarrRes = resultSpan();
      servarrWrap.appendChild(withResult(adminBtn(t('adm_connect_radarr'), '', function () {
        connectServarr('radarr', host.querySelector('.jc-c-RadarrUrl').value, host.querySelector('.jc-c-RadarrApiKey').value, radarrRes, { root: radarrRoot, profile: radarrProfile });
      }), radarrRes));

      servarrWrap.appendChild(sectionHeading(t('adm_sonarr_shows')));
      var sonarr = cfgForm(servarrWrap, cfg, [
        { key: 'SonarrUrl', label: t('cfg_sonarrurl'), type: 'text', placeholder: 'http://localhost:8989' },
        { key: 'SonarrApiKey', label: t('cfg_sonarrapikey'), type: 'secret' }
      ]);
      var sonarrRoot = servarrSelect(cfg.SonarrRootFolderPath, true);
      servarrWrap.appendChild(field(t('adm_sonarr_root_folder'), sonarrRoot));
      var sonarrProfile = servarrSelect(cfg.SonarrQualityProfileId, false);
      servarrWrap.appendChild(field(t('adm_sonarr_quality_profile'), sonarrProfile));
      var sonarrLang = servarrSelect(cfg.SonarrLanguageProfileId, false);
      servarrWrap.appendChild(field(t('adm_sonarr_language_profile'), sonarrLang));
      var sonarrRes = resultSpan();
      servarrWrap.appendChild(withResult(adminBtn(t('adm_connect_sonarr'), '', function () {
        connectServarr('sonarr', host.querySelector('.jc-c-SonarrUrl').value, host.querySelector('.jc-c-SonarrApiKey').value, sonarrRes, { root: sonarrRoot, profile: sonarrProfile, lang: sonarrLang });
      }), sonarrRes));

      servarrWrap.appendChild(sectionHeading(t('adm_prowlarr_optional')));
      var prowlarr = cfgForm(servarrWrap, cfg, [
        { key: 'ProwlarrUrl', label: t('cfg_prowlarrurl'), type: 'text' },
        { key: 'ProwlarrApiKey', label: t('cfg_prowlarrapikey'), type: 'secret' }
      ]);
      servarrWrap.appendChild(sectionHeading(t('adm_stalled_downloads')));
      var stalled = cfgForm(servarrWrap, cfg, [
        { key: 'RecoverStalledDownloads', label: t('cfg_recoverstalleddownloads'), type: 'check' },
        { key: 'StalledRecoveryMinutes', label: t('cfg_stalledrecoveryminutes'), type: 'num' }
      ]);
      host.appendChild(servarrWrap);

      var scriptWrap = document.createElement('div');
      var script = cfgForm(scriptWrap, cfg, [
        { type: 'section', label: t('cfgsec_local_script') },
        { key: 'ScriptPath', label: t('cfg_scriptpath'), type: 'text' },
        { key: 'ScriptArguments', label: t('cfg_scriptarguments'), type: 'text' }
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

      host.appendChild(sectionHeading(t('adm_test_save_first')));
      var testRes = resultSpan();
      host.appendChild(withResult(adminBtn(t('adm_test_backend'), '', function () { postTest('JellyCrowd/Download/Test', testRes, 'OK'); }), testRes));

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
    container.appendChild(sectionHeading(t('cfg_diagnostics')));
    var actions = document.createElement('div'); actions.className = 'jellycrowd-admin-actions';
    var diagRes = document.createElement('div'); diagRes.style.marginTop = '.6em';
    actions.appendChild(adminBtn(t('diag_run'), '', function () { runDiagnostics(diagRes); }));
    actions.appendChild(adminBtn(t('diag_backup'), '', function () { downloadBackup(); }));
    container.appendChild(actions);
    container.appendChild(diagRes);
    runDiagnostics(diagRes);

    container.appendChild(sectionHeading(t('adm_library_cleanup')));
    var orphans = checkbox('jc-cleanup-orphans', false);
    container.appendChild(field(t('adm_orphans_only_0_owners'), orphans));
    var scanRow = document.createElement('div'); scanRow.className = 'jellycrowd-admin-actions';
    var cleanupRes = document.createElement('div'); cleanupRes.style.marginTop = '.6em';
    scanRow.appendChild(adminBtn(t('adm_scan_media'), '', function () { scanCleanup(orphans.checked, cleanupRes); }));
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
      if (!items || !items.length) { box.textContent = t('adm_no_matching_media'); return; }
      items.forEach(function (m) {
        var row = document.createElement('div');
        row.style.cssText = 'display:flex;gap:.6em;padding:.4em 0;border-bottom:1px solid rgba(127,127,127,.2);align-items:center;';
        var label = m.Title + (m.Season != null ? ' — ' + t('season_number').replace('{n}', m.Season) : '');
        var name = document.createElement('span'); name.style.cssText = 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
        name.textContent = (m.MediaType === 'tv' ? '📺 ' : '🎬 ') + label + ' — ' + fmtBytes(m.SizeBytes) + ' · ' + m.OwnerCount + ' ' + t('ownership_owners');
        var del = adminBtn(t('comment_delete'), '', function (btn) {
          confirmAction({
            title: t('admin_delete_media'),
            message: t('adm_confirm_delete_disk').replace('{title}', label),
            confirmLabel: t('comment_delete'),
            danger: true
          }).then(function (ok) {
            if (!ok) { return; }
            btn.disabled = true;
            apiPostNoResult('JellyCrowd/Maintenance/Media/' + m.JellyfinItemId + '/Delete').then(function () { row.remove(); }).catch(function () { btn.disabled = false; });
          });
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

      // Bulk-edit bar: pick a property + value, apply it to every checked row (then Save persists, exactly
      // like editing rows by hand). Purely drives the same inputs, so it can't set anything Save can't.
      var bulk = document.createElement('div');
      bulk.className = 'jellycrowd-bulk-bar';
      var prop = document.createElement('select');
      prop.className = 'jellycrowd-bulk-prop';
      [['quota', t('adm_prop_quota')], ['can', t('adm_prop_canrequest')], ['auto', t('adm_prop_autoapprove')],
       ['cap', t('adm_prop_reqperiod')], ['access', t('adm_prop_pluginaccess')]].forEach(function (o) {
        var opt = document.createElement('option'); opt.value = o[0]; opt.textContent = o[1]; prop.appendChild(opt);
      });
      var valWrap = document.createElement('span');
      var valEl = null;
      function rebuildVal() {
        valWrap.innerHTML = '';
        var p = prop.value;
        if (p === 'quota' || p === 'cap') {
          valEl = document.createElement('input'); valEl.type = 'number'; valEl.min = '0'; valEl.step = '1';
          valEl.placeholder = p === 'quota' ? t('adm_prop_quota') : t('adm_prop_reqperiod');
        } else if (p === 'access') {
          valEl = document.createElement('select');
          [['', t('adm_default')], ['on', t('adm_opt_enabled')], ['off', t('adm_opt_disabled')]].forEach(function (o) {
            var opt = document.createElement('option'); opt.value = o[0]; opt.textContent = o[1]; valEl.appendChild(opt);
          });
        } else { // can / auto → yes/no
          valEl = document.createElement('select');
          [['yes', t('adm_bulk_yes')], ['no', t('adm_bulk_no')]].forEach(function (o) {
            var opt = document.createElement('option'); opt.value = o[0]; opt.textContent = o[1]; valEl.appendChild(opt);
          });
        }
        valEl.className = 'jellycrowd-bulk-val';
        valWrap.appendChild(valEl);
      }
      prop.addEventListener('change', rebuildVal);
      rebuildVal();
      var apply = adminBtn(t('adm_bulk_apply'), '', function () {
        var rows = Array.prototype.slice.call(container.querySelectorAll('tr[data-userid]'))
          .filter(function (r) { var c = r.querySelector('.jc-sel'); return c && c.checked; });
        if (!rows.length) { setMessage(t('adm_bulk_none_selected')); return; }
        var p = prop.value;
        rows.forEach(function (r) {
          if (p === 'quota') { r.querySelector('.jc-quota').value = valEl.value; }
          else if (p === 'cap') { r.querySelector('.jc-cap').value = valEl.value; }
          else if (p === 'can') { r.querySelector('.jc-can').checked = valEl.value === 'yes'; }
          else if (p === 'auto') { r.querySelector('.jc-auto').checked = valEl.value === 'yes'; }
          else if (p === 'access') { r.querySelector('.jc-access').value = valEl.value; }
        });
        setMessage(t('adm_bulk_applied').replace('{n}', rows.length));
      });
      bulk.appendChild(prop);
      bulk.appendChild(valWrap);
      bulk.appendChild(apply);
      container.appendChild(bulk);

      function usageText(uid) {
        var u = usage[uid];
        if (!u) { return '—'; }
        var used = (u.UsedBytes / GIB).toFixed(1);
        if (u.Unlimited || u.QuotaBytes <= 0) { return used + ' / ∞ GiB'; }
        return used + ' / ' + (u.QuotaBytes / GIB).toFixed(1) + ' GiB' + (u.Tier && u.Tier !== 'base' ? ' (' + u.Tier + ')' : '');
      }

      var table = document.createElement('table');
      table.className = 'jellycrowd-admin-table';
      table.innerHTML = '<thead><tr><th><input type="checkbox" id="jcQSelAll"></th><th>User</th><th>Usage</th><th>Quota (GiB)</th><th>Can request</th><th>Auto-approve</th><th>Req/period</th><th>Plugin access</th></tr></thead>';
      var tbody = document.createElement('tbody');
      users.forEach(function (user) {
        var ex = overrides.filter(function (o) { return o.UserId === user.Id; })[0] || {};
        var tr = document.createElement('tr');
        tr.setAttribute('data-userid', user.Id);
        function td(node) { var c = document.createElement('td'); c.appendChild(node); return c; }
        function textTd(text) { var c = document.createElement('td'); c.textContent = text; return c; }

        var sel = document.createElement('input');
        sel.type = 'checkbox';
        sel.className = 'jc-sel';
        tr.appendChild(td(sel));
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

      var selAll = table.querySelector('#jcQSelAll');
      if (selAll) {
        selAll.title = t('adm_bulk_selectall');
        selAll.addEventListener('change', function () {
          container.querySelectorAll('.jc-sel').forEach(function (c) { c.checked = selAll.checked; });
        });
      }

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
    i.placeholder = t('adm_default');
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

  // Keep at least one checkbox in a group selected: unchecking the last one is reverted (with a hint).
  function keepAtLeastOne(boxes) {
    boxes.forEach(function (cb) {
      cb.addEventListener('change', function () {
        if (!cb.checked && !boxes.some(function (b) { return b.checked; })) {
          cb.checked = true;
          setMessage(t('cfg_at_least_one'));
        }
      });
    });
  }

  // Per-user plugin-access override (3-state, overrides "config mode"): Default (follow config mode) /
  // Enabled (always) / Disabled (never). Maps to PluginAccess null / true / false.
  function accessSelect(value) {
    var s = document.createElement('select');
    s.className = 'jc-access';
    [['', 'Default'], ['on', t('adm_opt_enabled')], ['off', t('adm_opt_disabled')]].forEach(function (opt) {
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

  // ---------- User groups ----------
  // A group carries default Jelly Crowd settings its members inherit (a per-user override still wins) and
  // a set of libraries the admin can push onto members' Jellyfin accounts. A user belongs to one group.
  function triSelect(cls, value) {
    var s = document.createElement('select');
    s.className = cls;
    [['', t('adm_group_notset')], ['yes', t('adm_bulk_yes')], ['no', t('adm_bulk_no')]].forEach(function (o) {
      var opt = document.createElement('option'); opt.value = o[0]; opt.textContent = o[1]; s.appendChild(opt);
    });
    s.value = value === true ? 'yes' : (value === false ? 'no' : '');
    return s;
  }

  function triSelectAccess(cls, value) {
    var s = document.createElement('select');
    s.className = cls;
    [['', t('adm_group_notset')], ['on', t('adm_opt_enabled')], ['off', t('adm_opt_disabled')]].forEach(function (o) {
      var opt = document.createElement('option'); opt.value = o[0]; opt.textContent = o[1]; s.appendChild(opt);
    });
    s.value = value === true ? 'on' : (value === false ? 'off' : '');
    return s;
  }

  function labeledField(text, node) {
    var w = document.createElement('label');
    w.className = 'jellycrowd-group-field';
    var s = document.createElement('span');
    s.textContent = text;
    w.appendChild(s);
    w.appendChild(node);
    return w;
  }

  function newGuid() {
    if (window.crypto && typeof window.crypto.randomUUID === 'function') { return window.crypto.randomUUID(); }
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
      var r = Math.random() * 16 | 0;
      return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
  }

  // Read the group cards back into an array of group records (dropping unset optional fields via the lib).
  function collectGroups(host) {
    var result = [];
    host.querySelectorAll('.jellycrowd-group-card').forEach(function (card) {
      var members = [];
      card.querySelectorAll('.jc-g-member:checked').forEach(function (c) { members.push(c.value); });
      var libs = [];
      card.querySelectorAll('.jc-g-lib:checked').forEach(function (c) { libs.push(c.value); });
      result.push(lib.buildGroupRecord({
        id: card.getAttribute('data-groupid'),
        name: card.querySelector('.jc-g-name').value,
        members: members,
        libraryIds: libs,
        quotaGib: card.querySelector('.jc-g-quota').value,
        canRequest: card.querySelector('.jc-g-can').value,
        autoApprove: card.querySelector('.jc-g-auto').value,
        maxPerPeriod: card.querySelector('.jc-g-cap').value,
        pluginAccess: card.querySelector('.jc-g-access').value,
        childMode: card.querySelector('.jc-g-child').checked,
        childMaxAge: card.querySelector('.jc-g-childage').value
      }, GIB));
    });
    return result;
  }

  function renderGroups(container) {
    container.innerHTML = '';
    setMessage(t('loading'));
    if (!(window.ApiClient && window.ApiClient.getPluginConfiguration && window.ApiClient.getUsers && window.ApiClient.updatePluginConfiguration)) {
      setMessage(t('error_generic'));
      return;
    }
    Promise.all([
      window.ApiClient.getUsers(),
      window.ApiClient.getPluginConfiguration(PLUGIN_GUID),
      apiGet('Library/VirtualFolders').catch(function () { return []; })
    ]).then(function (res) {
      var users = res[0] || [];
      var cfg = res[1] || {};
      var libraries = (res[2] || []).filter(function (l) { return l && l.ItemId; });
      var groups = (cfg.UserGroups || []).slice();
      setMessage('');

      var hint = document.createElement('p');
      hint.className = 'jellycrowd-disclaimer';
      hint.textContent = t('adm_groups_hint');
      container.appendChild(hint);

      var addBtn = adminBtn(t('adm_group_add'), '', function () {
        groups = collectGroups(listHost);
        groups.push({ Id: newGuid(), Name: '', Members: [], LibraryIds: [] });
        renderList();
      });
      container.appendChild(addBtn);

      var listHost = document.createElement('div');
      listHost.className = 'jellycrowd-group-list';
      container.appendChild(listHost);

      function renderList() {
        listHost.innerHTML = '';
        if (!groups.length) {
          var empty = document.createElement('p');
          empty.className = 'jellycrowd-disclaimer';
          empty.textContent = t('adm_groups_empty');
          listHost.appendChild(empty);
          return;
        }
        groups.forEach(function (g) { listHost.appendChild(buildGroupCard(g)); });
      }

      function saveGroups() {
        var collected = collectGroups(listHost);
        groups = collected;
        return window.ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (live) {
          live.UserGroups = collected;
          return window.ApiClient.updatePluginConfiguration(PLUGIN_GUID, live);
        });
      }

      function buildGroupCard(g) {
        var card = document.createElement('div');
        card.className = 'jellycrowd-group-card';
        card.setAttribute('data-groupid', g.Id);

        var head = document.createElement('div');
        head.className = 'jellycrowd-group-head';
        var name = document.createElement('input');
        name.type = 'text';
        name.className = 'jc-g-name';
        name.placeholder = t('adm_group_name_ph');
        name.value = g.Name || '';
        head.appendChild(name);
        var del = adminBtn(t('admin_delete'), 'danger', function () {
          confirmAction({
            title: t('adm_group_delete_title'),
            message: t('adm_group_delete_msg').replace('{name}', g.Name || t('adm_group_unnamed')),
            confirmLabel: t('admin_delete'),
            danger: true
          }).then(function (ok) {
            if (!ok) { return; }
            groups = collectGroups(listHost).filter(function (x) { return x.Id !== g.Id; });
            renderList();
          });
        });
        head.appendChild(del);
        card.appendChild(head);

        var settings = document.createElement('div');
        settings.className = 'jellycrowd-group-settings';
        settings.appendChild(labeledField(t('adm_prop_quota'), numberInput('jc-g-quota', g.QuotaBytes != null ? (g.QuotaBytes / GIB) : '')));
        settings.appendChild(labeledField(t('adm_prop_canrequest'), triSelect('jc-g-can', g.CanRequest)));
        settings.appendChild(labeledField(t('adm_prop_autoapprove'), triSelect('jc-g-auto', g.AutoApprove)));
        settings.appendChild(labeledField(t('adm_prop_reqperiod'), numberInput('jc-g-cap', g.MaxRequestsPerPeriod != null ? g.MaxRequestsPerPeriod : '')));
        settings.appendChild(labeledField(t('adm_prop_pluginaccess'), triSelectAccess('jc-g-access', g.PluginAccess)));
        card.appendChild(settings);

        // Child mode: members get an age-filtered catalog (no search, no reviews/announcements).
        var childRow = document.createElement('div');
        childRow.className = 'jellycrowd-group-settings';
        var ageSel = document.createElement('select');
        ageSel.className = 'jc-g-childage';
        [['0', t('adm_child_age_all')], ['10', '10+'], ['12', '12+'], ['16', '16+']].forEach(function (o) {
          var op = document.createElement('option'); op.value = o[0]; op.textContent = o[1]; ageSel.appendChild(op);
        });
        ageSel.value = String(g.ChildMaxAge != null ? g.ChildMaxAge : 0);
        var childChk = checkbox('jc-g-child', g.ChildMode === true);
        ageSel.disabled = !childChk.checked;
        childChk.addEventListener('change', function () { ageSel.disabled = !childChk.checked; });
        childRow.appendChild(labeledField(t('adm_group_child_mode'), childChk));
        childRow.appendChild(labeledField(t('adm_group_child_max_age'), ageSel));
        card.appendChild(childRow);

        var memWrap = document.createElement('div');
        memWrap.className = 'jellycrowd-group-members';
        var memTitle = document.createElement('div');
        memTitle.className = 'jellycrowd-group-subtitle';
        memTitle.textContent = t('adm_group_members');
        memWrap.appendChild(memTitle);
        var memList = document.createElement('div');
        memList.className = 'jellycrowd-group-checklist';
        users.forEach(function (u) {
          var lbl = document.createElement('label');
          lbl.className = 'jellycrowd-group-check';
          var cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.className = 'jc-g-member';
          cb.value = u.Id;
          cb.checked = (g.Members || []).indexOf(u.Id) >= 0;
          cb.addEventListener('change', function () {
            if (cb.checked) {
              // One group per user: unselect this user in every other group card.
              listHost.querySelectorAll('.jc-g-member').forEach(function (other) {
                if (other !== cb && other.value === u.Id) { other.checked = false; }
              });
            }
          });
          lbl.appendChild(cb);
          lbl.appendChild(document.createTextNode(' ' + u.Name));
          memList.appendChild(lbl);
        });
        memWrap.appendChild(memList);
        card.appendChild(memWrap);

        var libWrap = document.createElement('div');
        libWrap.className = 'jellycrowd-group-libraries';
        var libTitle = document.createElement('div');
        libTitle.className = 'jellycrowd-group-subtitle';
        libTitle.textContent = t('adm_group_libraries');
        libWrap.appendChild(libTitle);
        var libList = document.createElement('div');
        libList.className = 'jellycrowd-group-checklist';
        libraries.forEach(function (l) {
          var lbl = document.createElement('label');
          lbl.className = 'jellycrowd-group-check';
          var cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.className = 'jc-g-lib';
          cb.value = l.ItemId;
          cb.checked = (g.LibraryIds || []).indexOf(l.ItemId) >= 0;
          lbl.appendChild(cb);
          lbl.appendChild(document.createTextNode(' ' + (l.Name || l.ItemId)));
          libList.appendChild(lbl);
        });
        libWrap.appendChild(libList);
        var applyBtn = adminBtn(t('adm_group_apply_libs'), '', function (btn) {
          confirmAction({
            title: t('adm_group_apply_title'),
            message: t('adm_group_apply_msg'),
            confirmLabel: t('adm_group_apply_confirm')
          }).then(function (ok) {
            if (!ok) { return; }
            btn.disabled = true;
            saveGroups().then(function () {
              return apiPostResult('JellyCrowd/Groups/' + encodeURIComponent(g.Id) + '/ApplyLibraryAccess');
            }).then(function (r) {
              btn.disabled = false;
              setMessage(t('adm_group_apply_done')
                .replace('{applied}', (r && r.Applied) || 0)
                .replace('{total}', (r && r.Total) || 0)
                .replace('{libs}', (r && r.Libraries) || 0));
            }).catch(function (e) { btn.disabled = false; setMessage(t(lib.errorKey(e && e.status))); });
          });
        });
        libWrap.appendChild(applyBtn);
        card.appendChild(libWrap);

        return card;
      }

      var save = adminBtn(t('save'), 'ok', function (btn) {
        btn.disabled = true;
        saveGroups().then(function () {
          btn.disabled = false;
          btn.textContent = t('saved');
          setTimeout(function () { btn.textContent = t('save'); }, 1500);
        }).catch(function (e) { btn.disabled = false; setMessage(t(lib.errorKey(e && e.status))); });
      });
      save.style.marginTop = '1em';

      renderList();
      container.appendChild(save);
    }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
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

    // Bulk retry: re-trigger a search for every approved-but-not-yet-available request (the stuck/blocked
    // ones). Mirrors the per-row Retry button; the server picks the set and reports how many were retried.
    var retryAll = adminBtn(t('admin_retry_all'), '', function (btn) {
      confirmAction({
        title: t('admin_retry_all_title'),
        message: t('admin_retry_all_msg'),
        confirmLabel: t('admin_retry_all')
      }).then(function (ok) {
        if (!ok) { return; }
        btn.disabled = true;
        apiPostResult('JellyCrowd/Requests/RetryAll').then(function (r) {
          btn.disabled = false;
          var done = t('admin_retry_all_done')
            .replace('{retried}', (r && r.Retried) || 0)
            .replace('{total}', (r && r.Total) || 0);
          reload().then(function () { setMessage(done); });
        }).catch(function (e) { btn.disabled = false; setMessage(t(lib.errorKey(e && e.status))); });
      });
    });
    bar.appendChild(retryAll);
    container.appendChild(bar);

    var listHost = document.createElement('div');
    container.appendChild(listHost);

    function reload() { return load(); }
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
      return apiGet('JellyCrowd/Requests').then(function (rows) { paint(rows || []); }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
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
        confirmAction({
          title: t('admin_delete_media'),
          message: t('confirm_delete_media'),
          confirmLabel: t('comment_delete'),
          danger: true
        }).then(function (ok) { if (ok) { adminDeleteMedia(request.Id); } });
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
        confirmAction({
          title: t('stats_import_pr'),
          message: t('stats_import_confirm'),
          confirmLabel: t('stats_import_pr')
        }).then(function (ok) {
          if (!ok) { return; }
          runImport();
        });
      });

      function runImport() {
        btn.disabled = true;
        setMessage(t('stats_importing'));
        apiPostResult('JellyCrowd/Stats/ImportPlaybackReporting').then(function (r) {
          btn.disabled = false;
          r = r || {};
          if (!r.Found) { setMessage(t('stats_import_none')); return; }
          setMessage(t('stats_import_done').replace('{n}', r.Imported || 0));
          load();
        }).catch(function (e) { btn.disabled = false; setMessage(t(lib.errorKey(e && e.status))); });
      }

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

      container.appendChild(sectionHeading(t('adm_header_icons')));
      var discEnable = checkbox('jc-b-disc-en', b.DiscordInviteEnabled === true);
      container.appendChild(field(t('adm_show_discord_icon_in_the_header'), discEnable));
      var discUrl = textInput('jc-b-disc-url', b.DiscordInviteUrl || '', 'https://discord.gg/…');
      container.appendChild(field(t('adm_discord_invite_link'), discUrl));
      var supEnable = checkbox('jc-b-sup-en', b.SupportLinkEnabled === true);
      container.appendChild(field(t('adm_show_support_donation_icon_in_the'), supEnable));
      var supUrl = textInput('jc-b-sup-url', b.SupportLinkUrl || '', 'https://…');
      container.appendChild(field(t('adm_support_donation_link'), supUrl));
      var guideEnable = checkbox('jc-b-guide-en', b.GuideLinkEnabled === true);
      container.appendChild(field(t('adm_show_user_guide_icon_in_the'), guideEnable));

      var hideDrawer = checkbox('jc-b-hidedrawer', b.HideNativeDrawerForNonAdmins === true);
      container.appendChild(field(t('adm_hide_drawer'), hideDrawer, t('adm_hide_drawer_hint')));

      container.appendChild(sectionHeading(t('adm_local_intros_pre_roll')));
      var liEnable = checkbox('jc-b-li-en', b.LocalIntrosEnabled === true);
      container.appendChild(field(t('adm_play_a_pre_roll_before_content'), liEnable, t('adm_drop_video_s_in_a_folder_hint')));
      var liFolder = textInput('jc-b-li-folder', b.LocalIntrosFolderName || 'intros', 'intros');
      container.appendChild(field(t('adm_pre_roll_folder_name'), liFolder));
      var liMovies = checkbox('jc-b-li-mov', b.LocalIntrosOnMovies !== false);
      container.appendChild(field(t('adm_before_movies'), liMovies));
      var liFirst = checkbox('jc-b-li-first', b.LocalIntrosOnFirstEpisode !== false);
      container.appendChild(field(t('adm_before_the_first_episode_of_a'), liFirst));
      var liNonSkip = checkbox('jc-b-li-ns', b.LocalIntrosNonSkippable !== false);
      container.appendChild(field(t('adm_non_skippable_web_client'), liNonSkip));
      var liWebOnly = checkbox('jc-b-li-webonly', b.LocalIntrosWebOnly !== false);
      container.appendChild(field(t('adm_web_and_desktop_players_only_recommended'), liWebOnly, t('adm_native_mobile_and_tv_apps_can_hint')));

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
          live.HideNativeDrawerForNonAdmins = hideDrawer.checked;
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
  // Tickets: a user reporting a problem, about a title or not. Open ones come first and oldest first —
  // the backlog is what needs attention, and the reminder e-mails talk about exactly this list.
  function renderReports(container) {
    container.innerHTML = '';

    var bar = document.createElement('div');
    bar.className = 'jellycrowd-admin-filter';
    var label = document.createElement('span');
    label.textContent = t('admin_filter_status');
    var filter = document.createElement('select');
    ['open', 'resolved', 'all'].forEach(function (value) {
      var opt = document.createElement('option');
      opt.value = value;
      filter.appendChild(opt);
    });
    bar.appendChild(label);
    bar.appendChild(filter);
    container.appendChild(bar);

    var listHost = document.createElement('div');
    container.appendChild(listHost);

    var rows = [];

    function countLabel(key, n) { return t(key).replace('{n}', String(n)); }

    function paint() {
      var open = rows.filter(function (r) { return !r.Resolved; });
      filter.options[0].textContent = countLabel('admin_reports_open', open.length);
      filter.options[1].textContent = countLabel('admin_reports_resolved', rows.length - open.length);
      filter.options[2].textContent = countLabel('admin_reports_all', rows.length);

      var mode = filter.value;
      var shown = lib.orderReports(rows, mode);

      listHost.innerHTML = '';
      if (!shown.length) {
        setMessage(rows.length ? (mode === 'open' ? t('admin_no_open_reports') : t('admin_no_reports')) : t('admin_no_reports'));
        return;
      }
      setMessage('');

      var table = document.createElement('table');
      table.className = 'jellycrowd-admin-table';
      var tbody = document.createElement('tbody');
      shown.forEach(function (r) { tbody.appendChild(reportRow(r)); });
      table.appendChild(tbody);
      listHost.appendChild(table);
    }

    function reportRow(r) {
      var tr = document.createElement('tr');
      if (r.Resolved) { tr.style.opacity = '0.55'; }
      var tdMain = document.createElement('td');
      var title = document.createElement('div');
      title.style.fontWeight = '600';
      // A report with no title stands on its own (playback, account…): say so rather than show a blank.
      title.textContent = (r.Title || t('report_general')) + (r.Resolved ? ' ✓' : '');
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
          apiPostJson('JellyCrowd/Reports/' + r.Id + '/Resolve', { Response: respInput.value }).then(load).catch(function () {});
        }));
      }
      tdA.appendChild(adminBtn(t('admin_delete'), 'danger', function () {
        apiPostNoResult('JellyCrowd/Reports/' + r.Id + '/Delete').then(load).catch(function () {});
      }));
      tr.appendChild(tdA);
      return tr;
    }

    function load() {
      setMessage(t('loading'));
      // Keep the chosen filter across a reload: resolving a ticket must not throw the admin back to
      // another view mid-triage.
      return apiGet('JellyCrowd/Reports')
        .then(function (reports) { rows = reports || []; paint(); })
        .catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
    }

    filter.addEventListener('change', paint);
    load();
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

  // ---------- Polls ----------
  // Polls are published with the announcements but managed here: a poll needs room for its options, its
  // audience and — since votes are nominative — who answered what and who has not answered at all.

  function pollGroupNames(poll, groups) {
    if (!poll.GroupIds || !poll.GroupIds.length) { return t('poll_everyone'); }
    var names = [];
    poll.GroupIds.forEach(function (id) {
      groups.forEach(function (g) { if (g.Id === id) { names.push(g.Name || id); } });
    });
    return names.join(', ') || t('poll_everyone');
  }

  // <input type="datetime-local"> wants a local "YYYY-MM-DDTHH:MM"; the API speaks UTC ISO.
  function toLocalInputValue(iso) {
    if (!iso) { return ''; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return ''; }
    var pad = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
  }

  function renderPolls(container) {
    container.innerHTML = '';
    setMessage(t('loading'));
    Promise.all([
      apiGet('JellyCrowd/Polls/All'),
      apiGet('JellyCrowd/Groups').catch(function () { return { Groups: [] }; })
    ]).then(function (res) {
      var polls = res[0] || [];
      var groups = (res[1] && res[1].Groups) || [];
      setMessage('');
      container.innerHTML = '';

      var editorHost = document.createElement('div');
      var listHost = document.createElement('div');
      listHost.className = 'jellycrowd-group-list';

      var hint = document.createElement('p');
      hint.className = 'jellycrowd-disclaimer';
      hint.textContent = t('poll_edit_locked');
      container.appendChild(hint);

      var addBtn = adminBtn(t('poll_new_full'), 'ok', function () { openEditor(null); });
      container.appendChild(addBtn);
      container.appendChild(editorHost);
      container.appendChild(listHost);

      function reload() { renderPolls(container); }

      function drawList() {
        listHost.innerHTML = '';
        if (!polls.length) {
          var empty = document.createElement('p');
          empty.className = 'jellycrowd-disclaimer';
          empty.textContent = t('poll_admin_empty');
          listHost.appendChild(empty);
          return;
        }
        polls.forEach(function (poll) { listHost.appendChild(buildPollCard(poll)); });
      }

      function buildPollCard(poll) {
        var card = document.createElement('div');
        card.className = 'jellycrowd-group-card';

        var head = document.createElement('div');
        head.className = 'jellycrowd-group-head';
        var question = document.createElement('strong');
        question.textContent = poll.Question;
        head.appendChild(question);

        var actions = document.createElement('div');
        actions.className = 'jcPollAdminActions';
        if (poll.Editable) {
          actions.appendChild(adminBtn(t('poll_edit'), '', function () { openEditor(poll); }));
        }

        actions.appendChild(adminBtn(poll.Closed ? t('poll_reopen') : t('poll_close_action'), '', function (btn) {
          btn.disabled = true;
          apiPostResult('JellyCrowd/Polls/' + poll.Id + (poll.Closed ? '/Reopen' : '/Close'))
            .then(reload)
            .catch(function () { btn.disabled = false; setMessage(t('error_generic')); });
        }));

        actions.appendChild(adminBtn(t('poll_delete'), 'danger', function () {
          confirmAction({
            title: t('poll_delete_title'),
            message: t('poll_delete_msg').replace('{question}', poll.Question),
            confirmLabel: t('poll_delete'),
            danger: true
          }).then(function (ok) {
            if (!ok) { return; }
            apiPostNoResult('JellyCrowd/Polls/' + poll.Id + '/Delete')
              .then(reload)
              .catch(function () { setMessage(t('error_generic')); });
          });
        }));
        head.appendChild(actions);
        card.appendChild(head);

        var meta = document.createElement('div');
        meta.className = 'jellycrowd-disclaimer';
        var bits = [poll.Open ? t('poll_open_state') : t('poll_closed'), pollGroupNames(poll, groups)];
        if (poll.ClosesAt) { bits.push(t('poll_closes_on').replace('{date}', new Date(poll.ClosesAt).toLocaleString())); }
        bits.push(t('poll_voters_count').replace('{count}', poll.TotalVotes));
        if (poll.MultiChoice) { bits.push(t('poll_multichoice')); }
        if (!poll.ShowResults) { bits.push(t('poll_results_hidden')); }
        meta.textContent = bits.join(' · ');
        card.appendChild(meta);

        var results = document.createElement('div');
        results.className = 'jcPollResults';
        (poll.Options || []).forEach(function (option) {
          var row = document.createElement('div');
          var rowHead = document.createElement('div');
          rowHead.className = 'jcPollResultHead';
          var name = document.createElement('span');
          name.textContent = option.Text;
          var value = document.createElement('span');
          value.textContent = option.Percent + '% (' + option.Votes + ')';
          rowHead.appendChild(name);
          rowHead.appendChild(value);
          row.appendChild(rowHead);
          var track = document.createElement('div');
          track.className = 'jcPollBarTrack';
          var bar = document.createElement('div');
          bar.className = 'jcPollBar';
          bar.style.width = lib.pollBarPercent(option) + '%';
          track.appendChild(bar);
          row.appendChild(track);
          if (option.Voters && option.Voters.length) {
            var voters = document.createElement('div');
            voters.className = 'jcPollVoters';
            voters.textContent = t('poll_voters') + ': ' + option.Voters.join(', ');
            row.appendChild(voters);
          }
          results.appendChild(row);
        });
        card.appendChild(results);

        if (poll.NotVoted && poll.NotVoted.length) {
          var pending = document.createElement('div');
          pending.className = 'jcPollVoters';
          pending.textContent = t('poll_not_voted') + ': ' + poll.NotVoted.join(', ');
          card.appendChild(pending);
        }

        return card;
      }

      // The editor doubles as the creation form: `poll` is null for a new one, and an existing poll can
      // only get here while it has no votes (the backend refuses the edit otherwise).
      function openEditor(poll) {
        editorHost.innerHTML = '';
        addBtn.disabled = true;

        var card = document.createElement('div');
        card.className = 'jellycrowd-group-card';

        var question = document.createElement('input');
        question.type = 'text';
        question.className = 'jc-poll-question';
        question.value = poll ? poll.Question : '';
        card.appendChild(labeledField(t('poll_question'), question));

        var optionsTitle = document.createElement('div');
        optionsTitle.className = 'jellycrowd-group-subtitle';
        optionsTitle.textContent = t('poll_options');
        card.appendChild(optionsTitle);

        var optionList = document.createElement('div');
        optionList.className = 'jellycrowd-group-checklist';
        card.appendChild(optionList);

        function addOptionRow(text) {
          var row = document.createElement('div');
          row.className = 'jcPollOptionRow';
          var input = document.createElement('input');
          input.type = 'text';
          input.className = 'jc-poll-option';
          input.value = text || '';
          row.appendChild(input);
          var remove = document.createElement('button');
          remove.type = 'button';
          remove.className = 'jcPollOptionRemove';
          remove.title = t('poll_remove_option');
          remove.setAttribute('aria-label', t('poll_remove_option'));
          remove.textContent = '×';
          remove.addEventListener('click', function () { row.remove(); });
          row.appendChild(remove);
          optionList.appendChild(row);
        }

        var existing = (poll && poll.Options) || [];
        if (existing.length) {
          existing.forEach(function (o) { addOptionRow(o.Text); });
        } else {
          addOptionRow('');
          addOptionRow('');
        }

        card.appendChild(adminBtn(t('poll_add_option'), '', function () { addOptionRow(''); }));

        var settings = document.createElement('div');
        settings.className = 'jellycrowd-group-settings';
        var multi = checkbox('jc-poll-multi', poll ? poll.MultiChoice : false);
        var showResults = checkbox('jc-poll-results', poll ? poll.ShowResults : true);
        var deadline = document.createElement('input');
        deadline.type = 'datetime-local';
        deadline.className = 'jc-poll-deadline';
        deadline.value = poll ? toLocalInputValue(poll.ClosesAt) : '';
        settings.appendChild(labeledField(t('poll_multichoice'), multi));
        settings.appendChild(labeledField(t('poll_showresults'), showResults));
        settings.appendChild(labeledField(t('poll_deadline'), deadline));
        card.appendChild(settings);

        var audience = document.createElement('div');
        audience.className = 'jellycrowd-group-members';
        var audienceTitle = document.createElement('div');
        audienceTitle.className = 'jellycrowd-group-subtitle';
        audienceTitle.textContent = t('announcement_audience');
        audience.appendChild(audienceTitle);
        var audienceList = document.createElement('div');
        audienceList.className = 'jellycrowd-group-checklist';
        groups.forEach(function (g) {
          var lbl = document.createElement('label');
          lbl.className = 'jellycrowd-group-check';
          var cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.className = 'jc-poll-group';
          cb.value = g.Id;
          cb.checked = !!(poll && poll.GroupIds && poll.GroupIds.indexOf(g.Id) >= 0);
          lbl.appendChild(cb);
          lbl.appendChild(document.createTextNode(g.Name || g.Id));
          audienceList.appendChild(lbl);
        });
        audience.appendChild(audienceList);
        var audienceHint = document.createElement('div');
        audienceHint.className = 'jellycrowd-disclaimer';
        audienceHint.textContent = t('poll_audience_hint');
        audience.appendChild(audienceHint);
        if (groups.length) { card.appendChild(audience); }

        var error = document.createElement('div');
        error.className = 'jellycrowd-disclaimer jcPollAdminError';
        error.hidden = true;
        card.appendChild(error);

        var buttons = document.createElement('div');
        buttons.className = 'jcPollAdminActions';
        buttons.appendChild(adminBtn(t('cancel'), '', function () { editorHost.innerHTML = ''; addBtn.disabled = false; }));
        buttons.appendChild(adminBtn(poll ? t('save') : t('poll_publish'), 'ok', function (btn) {
          var labels = [];
          [].forEach.call(optionList.querySelectorAll('.jc-poll-option'), function (i) {
            if (i.value.trim()) { labels.push(i.value.trim()); }
          });
          if (!question.value.trim()) { error.textContent = t('poll_question_required'); error.hidden = false; return; }
          if (labels.length < 2) { error.textContent = t('poll_min_options'); error.hidden = false; return; }

          var groupIds = [];
          [].forEach.call(audienceList.querySelectorAll('.jc-poll-group:checked'), function (c) { groupIds.push(c.value); });

          var body = {
            Question: question.value.trim(),
            Options: labels,
            MultiChoice: multi.checked,
            ShowResults: showResults.checked,
            ClosesAt: deadline.value ? new Date(deadline.value).toISOString() : null,
            GroupIds: groupIds
          };
          btn.disabled = true;
          error.hidden = true;
          apiPostJson('JellyCrowd/Polls' + (poll ? '/' + poll.Id : ''), body)
            .then(function () {
              // The header shell owns the badge and the takeover; tell it a poll just changed.
              if (typeof window.jellyCrowdRefreshPolls === 'function') { window.jellyCrowdRefreshPolls(); }
              reload();
            })
            .catch(function () { btn.disabled = false; error.textContent = t('error_generic'); error.hidden = false; });
        }));
        card.appendChild(buttons);

        editorHost.appendChild(card);
        question.focus();
      }

      drawList();
    }).catch(function (e) { setMessage(t(lib.errorKey(e && e.status))); });
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

  // A tab the shell asked us to open (window.jellyCrowdAdminTab), consumed once so it does not stick.
  function requestedTab() {
    var wanted = window.jellyCrowdAdminTab;
    window.jellyCrowdAdminTab = null;
    for (var i = 0; i < TABS.length; i++) {
      if (TABS[i].id === wanted) { return wanted; }
    }
    return null;
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
      activate(requestedTab() || TABS[0].id);
      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        // Re-opening the panel may also carry a tab request (the shell sets it before showing the view).
        window.jellyCrowdRegisterRefresh('admin', function () {
          var wanted = requestedTab();
          if (wanted || activeTab) { activate(wanted || activeTab); }
        });
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
