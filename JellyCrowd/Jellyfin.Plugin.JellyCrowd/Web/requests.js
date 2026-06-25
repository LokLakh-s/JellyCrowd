/*
 * Jelly Crowd — "My requests" page. Lists the current user's requests with their status.
 * Reuses pure helpers from catalog.lib.js; strings follow the Jellyfin/browser language.
 */
(function () {
  'use strict';

  var SUPPORTED_LANGS = ['en', 'fr'];
  var POSTER_BASE = 'https://image.tmdb.org/t/p/w154';
  var lib = window.JellyCrowdLib;
  var strings = {};
  var cfgLang = 'auto';
  var statusTimer = null;        // live status polling interval
  var STATUS_POLL_MS = 2000;
  var lastSignature = null;      // fingerprint of the rendered list, to re-render only on change
  var downloadingIds = {};       // requestId -> true for actively downloading/importing/queued items (for sort)
  var allowRetry = false;        // admin opt-in: regular users may trigger a manual retry-search
  var isAdmin = false;           // current user is an administrator

  function shortLang() {
    return lib.resolveLang(cfgLang, SUPPORTED_LANGS, navigator.language || 'en-US');
  }

  function loadConfigLang() {
    return apiGet('JellyCrowd/Settings/Language')
      .then(function (d) {
        if (d && d.Language) { cfgLang = String(d.Language).toLowerCase(); }
        allowRetry = !!(d && d.AllowUserRetrySearch === true);
      })
      .catch(function () { /* keep 'auto' on failure */ });
  }

  function resolveAdmin() {
    return apiGet('JellyCrowd/Settings/Visibility')
      .then(function (d) { isAdmin = !!(d && d.IsAdmin === true); })
      .catch(function () { /* non-admin / unavailable */ });
  }

  function t(key) {
    return Object.prototype.hasOwnProperty.call(strings, key) ? strings[key] : key;
  }

  // Navigate to a library item's Jellyfin details page (overlay auto-closes on hashchange).
  function openInJellyfin(itemId) {
    var serverId = (window.ApiClient && typeof window.ApiClient.serverId === 'function') ? window.ApiClient.serverId() : '';
    var hash = lib.jellyfinDetailsHash(itemId, serverId);
    if (hash) {
      window.location.hash = hash;
    }
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
      if (!r.ok) {
        throw new Error('HTTP ' + r.status);
      }

      return r.json();
    });
  }

  function apiPost(path) {
    if (window.ApiClient && typeof window.ApiClient.ajax === 'function') {
      return window.ApiClient.ajax({ type: 'POST', url: pluginUrl(path) });
    }
    return fetch(pluginUrl(path), { method: 'POST' }).then(function (r) {
      if (!r.ok) {
        throw new Error('HTTP ' + r.status);
      }
      return r;
    });
  }

  function loadStrings() {
    return fetch(pluginUrl('JellyCrowd/Web/strings/' + shortLang() + '.json'))
      .then(function (r) { return r.ok ? r.json() : {}; })
      .catch(function () { return {}; })
      .then(function (loaded) { strings = loaded || {}; });
  }

  function setMessage(text) {
    var el = document.getElementById('jcReqMessage');
    if (text) {
      el.textContent = text;
      el.hidden = false;
    } else {
      el.hidden = true;
    }
  }

  function renderRow(request) {
    var row = document.createElement('div');
    row.className = 'jellycrowd-request-row';
    row.dataset.reqId = request.Id;
    row._jcRequests = [request]; // for autosort

    var available = (request.Status === 3 || request.Status === 'Available');
    // Clicking the title or poster opens the media detail popup (shared from the catalog view).
    function openDetail() {
      if (typeof window.jellyCrowdOpenDetail === 'function') {
        window.jellyCrowdOpenDetail({
          TmdbId: request.TmdbId,
          MediaType: request.MediaType,
          Title: request.Title,
          PosterPath: request.PosterPath,
          ReleaseDate: request.ReleaseDate,
          Available: available,
          JellyfinItemId: request.JellyfinItemId
        });
      }
    }

    if (request.PosterPath) {
      var poster = document.createElement('img');
      poster.className = 'jellycrowd-request-poster jellycrowd-link';
      poster.loading = 'lazy';
      poster.alt = request.Title || '';
      poster.src = POSTER_BASE + request.PosterPath;
      poster.addEventListener('click', openDetail);
      row.appendChild(poster);
    } else {
      var empty = document.createElement('div');
      empty.className = 'jellycrowd-request-poster jellycrowd-link';
      empty.addEventListener('click', openDetail);
      row.appendChild(empty);
    }

    var main = document.createElement('div');
    main.className = 'jellycrowd-request-main';

    var titleEl = document.createElement('div');
    titleEl.className = 'jellycrowd-request-title jellycrowd-link';
    titleEl.textContent = lib.formatTitle(request)
      + (request.Season ? ' · S' + request.Season : '')
      + (request.Episode ? 'E' + request.Episode : '');
    titleEl.title = t('details_button');
    titleEl.addEventListener('click', openDetail);
    main.appendChild(titleEl);

    var dateParts = [];
    if (request.RequestedAt) {
      dateParts.push(t('requested_on') + ' ' + new Date(request.RequestedAt).toLocaleDateString());
    }
    if (available && request.AvailableAt) {
      dateParts.push(t('available_on') + ' ' + new Date(request.AvailableAt).toLocaleDateString());
    }
    if (dateParts.length) {
      var sub = document.createElement('div');
      sub.className = 'jellycrowd-request-sub';
      sub.textContent = dateParts.join(' · ');
      main.appendChild(sub);
    }
    row.appendChild(main);

    var status = document.createElement('span');
    if (request.DeletionRequestedAt) {
      status.className = 'jellycrowd-status jellycrowd-status-denied';
      status.textContent = t('deletion_requested');
    } else {
      var key = lib.statusLabelKey(request.Status);
      status.className = 'jellycrowd-status jellycrowd-status-' + key.replace('status_', '');
      status.textContent = t(key);
    }
    row.appendChild(status);

    // A dispatched-but-still-approved request that carries a backend error is "blocked" (not found /
    // backend issue) — surfaced distinctly from a normal in-progress request, with the reason on hover.
    var approved = (request.Status === 1 || request.Status === 'Approved');
    if (approved && request.DispatchError && !request.DeletionRequestedAt) {
      var blocked = document.createElement('span');
      blocked.className = 'jellycrowd-status jellycrowd-status-denied';
      blocked.textContent = t('status_blocked');
      blocked.title = request.DispatchError;
      row.appendChild(blocked);
    }

    // Show a "scheduled for <date>" hint when fulfillment is deferred to a future (release) date.
    var desired = request.DesiredAt ? new Date(request.DesiredAt) : null;
    if (desired && desired.getTime() > Date.now() && !request.DeletionRequestedAt
        && (request.Status === 0 || request.Status === 'Pending' || request.Status === 1 || request.Status === 'Approved')) {
      var scheduled = document.createElement('span');
      scheduled.className = 'jellycrowd-status jellycrowd-status-scheduled';
      scheduled.textContent = t('scheduled_for') + ' ' + desired.toLocaleDateString();
      row.appendChild(scheduled);

      // For an unreleased / deferred request, spell out the release date + the next search attempt.
      var unrelParts = [];
      if (request.ReleaseDate) {
        var rd = new Date(request.ReleaseDate);
        unrelParts.push(t('release_date') + ' ' + (isNaN(rd.getTime()) ? request.ReleaseDate : rd.toLocaleDateString()));
      }
      unrelParts.push(t('next_attempt') + ' ' + desired.toLocaleDateString());
      var sub2 = document.createElement('div');
      sub2.className = 'jellycrowd-request-sub';
      sub2.textContent = unrelParts.join(' · ');
      main.appendChild(sub2);
    }

    // Cancellable while pending or approved (an approved request also asks the backend to stop).
    var cancellable = (request.Status === 0 || request.Status === 'Pending'
      || request.Status === 1 || request.Status === 'Approved');
    if (cancellable && !request.DeletionRequestedAt) {
      var cancel = document.createElement('button');
      cancel.type = 'button';
      cancel.className = 'jellycrowd-request';
      cancel.textContent = t('cancel');
      cancel.addEventListener('click', function () {
        cancel.disabled = true;
        apiPost('JellyCrowd/Requests/' + request.Id + '/Cancel')
          .then(function () { row.remove(); refreshQuota(); })
          .catch(function () { cancel.disabled = false; });
      });
      row.appendChild(cancel);
    }

    // Approved requests can have their search re-triggered (Radarr/Sonarr) — useful when a release was
    // not found yet or the dispatch had failed. Admin-only unless the admin opted users in.
    if (approved && !request.DeletionRequestedAt && (allowRetry || isAdmin)) {
      var retry = document.createElement('button');
      retry.type = 'button';
      retry.className = 'jellycrowd-request';
      retry.textContent = t('retry_search');
      retry.addEventListener('click', function () {
        retry.disabled = true;
        apiPost('JellyCrowd/Requests/' + request.Id + '/Retry')
          .then(function () { lastSignature = ''; tick(); })
          .catch(function () { retry.disabled = false; });
      });
      row.appendChild(retry);
    }

    return row;
  }

  function renderQuota(info) {
    var el = document.getElementById('jcQuota');
    el.innerHTML = '';
    if (!info) {
      return;
    }

    // Clicking the quota opens the "My media" view to manage/delete owned titles.
    el.classList.add('jellycrowd-quota-clickable');
    el.title = t('my_media_title');
    el.onclick = function () {
      if (typeof window.jellyCrowdShowView === 'function') {
        window.jellyCrowdShowView('mymedia');
      } else {
        window.location.hash = '#/userpluginsettings.html?pageUrl=' + encodeURIComponent('/JellyCrowd/Web/mymedia.html');
      }
    };

    var unlimited = info.Unlimited || info.QuotaBytes <= 0;
    var label = document.createElement('div');
    label.className = 'jellycrowd-quota-label';
    label.style.color = '#fff';
    label.textContent = t('quota_storage') + ' : ' + lib.formatBytes(info.UsedBytes)
      + ' / ' + (unlimited ? t('quota_unlimited') : lib.formatBytes(info.QuotaBytes));
    el.appendChild(label);

    if (!unlimited) {
      var percent = lib.quotaPercent(info.UsedBytes, info.QuotaBytes);
      var track = document.createElement('div');
      track.className = 'jellycrowd-quota-track';
      var fill = document.createElement('div');
      fill.className = 'jellycrowd-quota-fill';
      fill.style.width = percent + '%';
      // Grade green -> yellow -> red by fill level (overrides the static CSS colour).
      fill.style.background = lib.quotaColor(percent);
      track.appendChild(fill);
      el.appendChild(track);
    }
  }

  // Apply live download statuses (from the Radarr/Sonarr queue) onto the matching rows. Stale
  // badges are cleared first so a finished download stops showing progress.
  // Decimal byte formatter (GB = /1000) to match RDT/Radarr's reported sizes (the quota bar uses binary GiB).
  function formatBytesDecimal(n) {
    n = Number(n) || 0;
    var u = ['B', 'KB', 'MB', 'GB', 'TB'];
    var i = 0;
    while (n >= 1000 && i < u.length - 1) { n /= 1000; i++; }
    return (i === 0 ? n : n.toFixed(1)) + ' ' + u[i];
  }

  function applyDownloadStatuses(statuses) {
    var list = document.getElementById('jcReqList');
    if (!list) {
      return;
    }

    Array.prototype.forEach.call(list.querySelectorAll('.jellycrowd-dl'), function (el) { el.remove(); });

    // Track which requests are actively downloading (for the "Downloading" sort tier).
    downloadingIds = {};
    (statuses || []).forEach(function (s) {
      if (s.State === 'downloading' || s.State === 'importing' || s.State === 'queued') {
        downloadingIds[s.RequestId] = true;
      }
    });

    (statuses || []).forEach(function (s) {
      var row = list.querySelector('.jellycrowd-request-row[data-req-id="' + s.RequestId + '"]');
      if (!row) {
        // Aggregated season row: match any of its episode request ids.
        row = Array.prototype.find.call(list.querySelectorAll('.jellycrowd-request-row[data-req-ids]'), function (el) {
          return (' ' + el.dataset.reqIds + ' ').indexOf(' ' + s.RequestId + ' ') >= 0;
        });
      }
      if (!row) {
        return;
      }
      // One progress badge per aggregated row (the first matching episode wins this cycle).
      if (row.dataset.reqIds && row.querySelector('.jellycrowd-dl')) {
        return;
      }

      var key = lib.downloadStateKey(s.State);
      var badge = document.createElement('span');
      badge.className = 'jellycrowd-status jellycrowd-dl jellycrowd-dl-' + s.State;
      var label = key ? t(key) : s.State;
      if (s.State === 'downloading' || s.State === 'importing') {
        label += ' ' + Math.round(s.Percent || 0) + '%';
        // Final size is known up front with debrid (RDT) — show it while downloading. Use DECIMAL
        // units (GB, /1000) to match what RDT/Radarr display, not binary GiB.
        if (s.SizeBytes > 0) {
          label += ' · ' + formatBytesDecimal(s.SizeBytes);
        }
        if (s.TimeLeft) {
          label += ' · ' + s.TimeLeft;
        }
      }
      badge.textContent = label;
      // Insert before the Cancel button when present, otherwise at the end.
      var cancelBtn = row.querySelector('button.jellycrowd-request');
      row.insertBefore(badge, cancelBtn || null);
    });

    // Re-sort now that we know which rows are downloading (the "Downloading" tier).
    sortRows();
  }

  function pollDownloadStatus() {
    // Cache-buster: ApiClient/the browser would otherwise serve a cached GET, freezing the
    // progress until a full page reload.
    apiGet('JellyCrowd/Requests/Mine/DownloadStatus?ts=' + Date.now())
      .then(applyDownloadStatuses)
      .catch(function () { /* best-effort */ });
  }

  // N15: collapse the per-episode requests of one season into a single row, so an in-progress season
  // (which is fulfilled episode-by-episode in the backend) shows as one line instead of flooding the
  // list. A lone episode request stays a normal row.
  function isEpisodeRequest(r) {
    return r.MediaType === 'tv' && r.Season != null && r.Episode != null;
  }

  function statusInt(r) {
    if (typeof r.Status === 'number') { return r.Status; }
    var map = { Pending: 0, Approved: 1, Denied: 2, Available: 3 };
    return map[r.Status] != null ? map[r.Status] : 0;
  }

  // Sort tier for a request — lower sorts higher. Order: Pending, Approved, Downloading, Deletion
  // requested, Unreleased (scheduled), Available, Denied.
  function rankOf(r) {
    if (r.DeletionRequestedAt) { return 3; }
    var st = statusInt(r);
    if (st === 3) { return 5; } // available
    if (st === 2) { return 6; } // denied
    if (r.DesiredAt && new Date(r.DesiredAt).getTime() > Date.now()) { return 4; } // unreleased / scheduled
    if (downloadingIds[r.Id]) { return 2; } // downloading
    if (st === 1) { return 1; } // approved
    return 0; // pending
  }

  // A row's tier is the highest-priority (lowest) tier among its request(s) — an aggregated season row
  // carries several episode requests.
  function rowRank(row) {
    var reqs = row._jcRequests || [];
    var min = 99;
    reqs.forEach(function (r) { var k = rankOf(r); if (k < min) { min = k; } });
    return min;
  }

  function sortRows() {
    var list = document.getElementById('jcReqList');
    if (!list) { return; }
    var rows = Array.prototype.slice.call(list.querySelectorAll('.jellycrowd-request-row'));
    rows.sort(function (a, b) { return rowRank(a) - rowRank(b); }); // Array.sort is stable → ties keep order
    rows.forEach(function (row) { list.appendChild(row); });
  }

  // Aggregated row for all requested episodes of one season.
  function renderSeasonGroupRow(group) {
    var first = group[0];
    var row = document.createElement('div');
    row.className = 'jellycrowd-request-row';
    row.dataset.reqIds = group.map(function (r) { return r.Id; }).join(' ');
    row.dataset.reqId = first.Id; // representative, for compatibility
    row._jcRequests = group; // for autosort

    function openDetail() {
      if (typeof window.jellyCrowdOpenDetail === 'function') {
        // Open the SERIES popup (no season) → the season picker, so the user can manage all seasons.
        window.jellyCrowdOpenDetail({
          TmdbId: first.TmdbId, MediaType: 'tv', Title: first.Title,
          PosterPath: first.PosterPath, ReleaseDate: first.ReleaseDate,
          Available: false, JellyfinItemId: first.JellyfinItemId
        });
      }
    }

    var posterEl = document.createElement(first.PosterPath ? 'img' : 'div');
    posterEl.className = 'jellycrowd-request-poster jellycrowd-link';
    if (first.PosterPath) { posterEl.loading = 'lazy'; posterEl.alt = first.Title || ''; posterEl.src = POSTER_BASE + first.PosterPath; }
    posterEl.addEventListener('click', openDetail);
    row.appendChild(posterEl);

    var main = document.createElement('div');
    main.className = 'jellycrowd-request-main';
    var titleEl = document.createElement('div');
    titleEl.className = 'jellycrowd-request-title jellycrowd-link';
    titleEl.textContent = lib.formatTitle(first) + ' · ' + t('season_label') + ' ' + first.Season;
    titleEl.title = t('details_button');
    titleEl.addEventListener('click', openDetail);
    main.appendChild(titleEl);

    var total = group.length;
    var availableCount = group.filter(function (r) { return statusInt(r) === 3; }).length;

    var sub = document.createElement('div');
    sub.className = 'jellycrowd-request-sub';
    sub.textContent = t('episodes_available').replace('{a}', availableCount).replace('{b}', total);
    main.appendChild(sub);

    // Next episode date: the earliest future release/desired date among not-yet-available episodes.
    var now = Date.now();
    var nextTs = null;
    group.forEach(function (r) {
      if (statusInt(r) === 3) { return; }
      var d = r.ReleaseDate ? new Date(r.ReleaseDate) : (r.DesiredAt ? new Date(r.DesiredAt) : null);
      if (d && !isNaN(d.getTime()) && d.getTime() > now && (nextTs === null || d.getTime() < nextTs)) {
        nextTs = d.getTime();
      }
    });
    if (nextTs !== null) {
      var nextSub = document.createElement('div');
      nextSub.className = 'jellycrowd-request-sub';
      nextSub.textContent = t('next_episode') + ' ' + new Date(nextTs).toLocaleDateString();
      main.appendChild(nextSub);
    }
    row.appendChild(main);

    // Aggregate status badge.
    var allDeletion = group.every(function (r) { return r.DeletionRequestedAt; });
    var status = document.createElement('span');
    if (allDeletion) {
      status.className = 'jellycrowd-status jellycrowd-status-denied';
      status.textContent = t('deletion_requested');
    } else if (availableCount === total) {
      status.className = 'jellycrowd-status jellycrowd-status-available';
      status.textContent = t('status_available');
    } else if (availableCount > 0) {
      status.className = 'jellycrowd-status jellycrowd-status-scheduled';
      status.textContent = availableCount + '/' + total;
    } else {
      var key = lib.statusLabelKey(first.Status);
      status.className = 'jellycrowd-status jellycrowd-status-' + key.replace('status_', '');
      status.textContent = t(key);
    }
    row.appendChild(status);

    // Cancel every still-active episode of the season at once.
    var cancellable = group.filter(function (r) {
      return !r.DeletionRequestedAt && (statusInt(r) === 0 || statusInt(r) === 1);
    });
    if (cancellable.length) {
      var cancel = document.createElement('button');
      cancel.type = 'button';
      cancel.className = 'jellycrowd-request';
      cancel.textContent = t('cancel');
      cancel.addEventListener('click', function () {
        cancel.disabled = true;
        Promise.all(cancellable.map(function (r) { return apiPost('JellyCrowd/Requests/' + r.Id + '/Cancel').catch(function () {}); }))
          .then(function () { lastSignature = ''; tick(); });
      });
      row.appendChild(cancel);
    }

    // Retry every approved episode of the season at once (admin / opted-in users).
    var retriable = group.filter(function (r) { return statusInt(r) === 1 && !r.DeletionRequestedAt; });
    if (retriable.length && (allowRetry || isAdmin)) {
      var retry = document.createElement('button');
      retry.type = 'button';
      retry.className = 'jellycrowd-request';
      retry.textContent = t('retry_search');
      retry.addEventListener('click', function () {
        retry.disabled = true;
        Promise.all(retriable.map(function (r) { return apiPost('JellyCrowd/Requests/' + r.Id + '/Retry').catch(function () {}); }))
          .then(function () { lastSignature = ''; tick(); });
      });
      row.appendChild(retry);
    }

    return row;
  }

  function render(requests) {
    var list = document.getElementById('jcReqList');
    list.innerHTML = '';

    if (!requests || requests.length === 0) {
      setMessage(t('no_requests'));
      return;
    }

    setMessage('');

    // Pre-group episode requests by (TmdbId, Season); only collapse when there's more than one.
    var groups = {};
    requests.forEach(function (r) {
      if (!isEpisodeRequest(r)) { return; }
      var key = r.TmdbId + ':' + r.Season;
      (groups[key] = groups[key] || []).push(r);
    });

    var renderedGroups = {};
    requests.forEach(function (request) {
      if (!isEpisodeRequest(request)) { list.appendChild(renderRow(request)); return; }
      var key = request.TmdbId + ':' + request.Season;
      if (groups[key].length < 2) { list.appendChild(renderRow(request)); return; }
      if (renderedGroups[key]) { return; }
      renderedGroups[key] = true;
      list.appendChild(renderSeasonGroupRow(groups[key]));
    });

    sortRows();
  }

  // Compact fingerprint of the list so the live tick only re-renders when something actually changed
  // (status flip, new/removed request, deletion flag) — avoids rebuilding rows every few seconds.
  function signatureOf(requests) {
    return (requests || []).map(function (r) {
      return r.Id + ':' + r.Status + ':' + (r.DeletionRequestedAt ? 1 : 0);
    }).join('|');
  }

  function refreshQuota() {
    apiGet('JellyCrowd/Quota/Me?ts=' + Date.now())
      .then(renderQuota)
      .catch(function () { /* quota bar is best-effort */ });
    // Keep the header quota bar in sync too (M28).
    if (typeof window.jellyCrowdRefreshQuota === 'function') { window.jellyCrowdRefreshQuota(); }
  }

  // Live tick: pick up status transitions (e.g. Approved -> Available) without a page reload and
  // refresh download progress. Re-renders only on a real change, to avoid flicker.
  function tick() {
    apiGet('JellyCrowd/Requests/Mine?ts=' + Date.now())
      .then(function (requests) {
        var sig = signatureOf(requests);
        if (sig !== lastSignature) {
          lastSignature = sig;
          render(requests);
          refreshQuota();
        }
        pollDownloadStatus();
      })
      .catch(function () { /* best-effort; keep the current view */ });
  }

  // Full refresh used on first load and whenever the overlay re-shows this view.
  function refresh() {
    refreshQuota();
    apiGet('JellyCrowd/Requests/Mine?ts=' + Date.now())
      .then(function (requests) {
        lastSignature = signatureOf(requests);
        render(requests);
        pollDownloadStatus();
      })
      .catch(function () { setMessage(t('error_generic')); });
  }

  function init() {
    loadConfigLang().then(resolveAdmin).then(loadStrings).then(function () {
      document.getElementById('jcReqLogo').src = pluginUrl('JellyCrowd/Web/logo.png');
      document.getElementById('jcReqTitle').textContent = t('my_requests_title');
      document.getElementById('jcReqDisclaimer').textContent = t('requests_latency_disclaimer');
      setMessage(t('loading'));

      if (typeof window.jellyCrowdRegisterRefresh === 'function') {
        window.jellyCrowdRegisterRefresh('requests', refresh);
      }

      // Poll the live download status while this view is loaded (single interval).
      if (statusTimer) {
        clearInterval(statusTimer);
      }
      statusTimer = setInterval(tick, STATUS_POLL_MS);

      refresh();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
